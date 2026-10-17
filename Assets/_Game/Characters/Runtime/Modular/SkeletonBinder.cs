using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Characters.Modular
{
    /// <summary>
    /// Finds a character's bones by name and rebinds skinned meshes onto them. This works because every part is
    /// skinned to an identical rest skeleton, so the part mesh's bind poses stay valid on the character's bones.
    /// Bone names must be unique, including bones that only one part has (prefix them, e.g. "Hair.Ponytail.1").
    /// </summary>
    public sealed class SkeletonBinder
    {
        readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>(StringComparer.Ordinal);

        public SkeletonBinder(Transform skeletonRoot)
        {
            foreach (Transform bone in skeletonRoot.GetComponentsInChildren<Transform>(true))
                Register(bone);
        }

        public bool TryGetBone(string name, out Transform bone) => _bones.TryGetValue(name, out bone);

        /// <summary>
        /// Points <paramref name="renderer"/> at this skeleton's bones. Bones only the part has (hair, skirt or
        /// accessory bones) are grafted, with their children, under the nearest ancestor this skeleton does have, so
        /// they follow the body. Returns the roots of the grafted subtrees; remove them with the part via <see cref="Forget"/>.
        /// </summary>
        public List<Transform> Rebind(SkinnedMeshRenderer renderer)
        {
            var grafted = new List<Transform>();
            Transform[] bones = renderer.bones;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null)
                    bones[i] = Resolve(bones[i], grafted);
            renderer.bones = bones;

            if (renderer.rootBone != null)
                renderer.rootBone = Resolve(renderer.rootBone, grafted);
            return grafted;
        }

        /// <summary>Unregisters a subtree that <see cref="Rebind"/> grafted. The caller destroys it.</summary>
        public void Forget(Transform graftedRoot)
        {
            foreach (Transform bone in graftedRoot.GetComponentsInChildren<Transform>(true))
                _bones.Remove(bone.name);
        }

        Transform Resolve(Transform partBone, List<Transform> grafted)
        {
            if (_bones.TryGetValue(partBone.name, out Transform bone)) return bone;

            // Graft from the topmost ancestor we lack, under the nearest ancestor we have.
            Transform top = partBone;
            Transform anchor = null;
            for (Transform ancestor = partBone.parent; ancestor != null; ancestor = ancestor.parent)
            {
                if (_bones.TryGetValue(ancestor.name, out anchor)) break;
                top = ancestor;
            }
            if (anchor == null)
                throw new InvalidOperationException($"Bone '{partBone.name}' has no ancestor in the character's skeleton.");

            top.SetParent(anchor, worldPositionStays: false); // rest poses match, so local transforms carry over
            foreach (Transform added in top.GetComponentsInChildren<Transform>(true))
                Register(added);
            grafted.Add(top);
            return partBone;
        }

        void Register(Transform bone)
        {
            if (_bones.ContainsKey(bone.name))
                throw new ArgumentException($"Two bones are named '{bone.name}'; bone names must be unique.");
            _bones.Add(bone.name, bone);
        }
    }
}
