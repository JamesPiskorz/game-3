using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Modular;
using UnityEngine;

namespace Game.Characters.Springs
{
    /// <summary>Where a character's spring colliders go (per body, from content).</summary>
    [Serializable]
    public sealed class SpringColliderBinding
    {
        public string Bone;
        public Vector3 Offset;
        public float Radius = 0.1f;
    }

    /// <summary>
    /// Keeps a character's spring chains in step with what it wears: one chain per spring root declared by an
    /// equipped part, removed when the part comes off, all sharing the body's colliders.
    /// </summary>
    public static class SpringBoneRig
    {
        public static void Sync(ModularCharacter character, IReadOnlyList<SpringColliderBinding> colliders)
        {
            Transform root = character.transform;
            List<SpringCollider> bodyColliders = EnsureColliders(root, colliders);

            var wanted = new HashSet<Transform>();
            foreach (string slot in character.EquippedSlots)
            {
                CharacterPart part = character.GetPart(slot);
                foreach (string bone in part.SpringChains ?? Array.Empty<string>())
                {
                    Transform chainRoot = Find(root, bone);
                    if (chainRoot != null) wanted.Add(chainRoot);
                }
            }

            foreach (SpringBoneChain chain in root.GetComponents<SpringBoneChain>())
            {
                if (chain.Root == null || !wanted.Contains(chain.Root)) DestroySafely(chain);
                else wanted.Remove(chain.Root);
            }

            foreach (Transform chainRoot in wanted)
            {
                var chain = character.gameObject.AddComponent<SpringBoneChain>();
                chain.Root = chainRoot;
                chain.Colliders = bodyColliders;
                chain.Initialize();
            }
        }

        static List<SpringCollider> EnsureColliders(Transform root, IReadOnlyList<SpringColliderBinding> bindings)
        {
            List<SpringCollider> existing = root.GetComponentsInChildren<SpringCollider>(true).ToList();
            if (existing.Count > 0 || bindings == null) return existing;

            foreach (SpringColliderBinding binding in bindings)
            {
                Transform bone = Find(root, binding.Bone);
                if (bone == null) continue;
                var collider = bone.gameObject.AddComponent<SpringCollider>();
                collider.Offset = binding.Offset;
                collider.Radius = binding.Radius;
                existing.Add(collider);
            }
            return existing;
        }

        static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        static void DestroySafely(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
