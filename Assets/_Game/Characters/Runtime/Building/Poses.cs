using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game.Characters.Building
{
    /// <summary>Simple bone poses for previews and review renders (stand-in skeleton names) until animation exists.</summary>
    public static class Poses
    {
        /// <summary>
        /// Arms down with a slight bend and feet a little apart, so characters can stand close together in a lineup. Call
        /// with the skeleton at rest. Each bend is given in the character's frame relative to the parent's bend, so it
        /// poses any rig with these bone names the same way, whatever its bones' own axes (Blender exports keep
        /// Blender's bone roll). Tools/Blender/body_tools.RELAXED is this pose for fitting garments: keep them in step.
        /// </summary>
        public static void Relaxed(Transform character)
        {
            var pose = new Pose(character);
            pose.Bend("UpperArm.L", new Vector3(0f, 8f, 72f));
            pose.Bend("UpperArm.R", new Vector3(0f, -8f, -72f));
            pose.Bend("LowerArm.L", new Vector3(0f, 12f, 0f));
            pose.Bend("LowerArm.R", new Vector3(0f, -12f, 0f));
            // Feet a little apart, as people stand: straight down, generated bodies' thighs merge into one shape.
            pose.Bend("UpperLeg.L", new Vector3(0f, 0f, -3f));
            pose.Bend("UpperLeg.R", new Vector3(0f, 0f, 3f));
        }

        sealed class Pose
        {
            readonly Quaternion _root;
            readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
            readonly Dictionary<Transform, Quaternion> _rest = new Dictionary<Transform, Quaternion>();

            public Pose(Transform character)
            {
                _root = character.rotation;
                foreach (Transform bone in character.GetComponentsInChildren<Transform>(true))
                {
                    if (!_bones.ContainsKey(bone.name)) _bones[bone.name] = bone;
                    _rest[bone] = bone.rotation;
                }
            }

            // The rotation a bone has gained since rest, expressed in the character's frame.
            Quaternion Delta(Transform bone) =>
                bone == null || !_rest.ContainsKey(bone)
                    ? Quaternion.identity
                    : Quaternion.Inverse(_root) * bone.rotation * Quaternion.Inverse(_rest[bone]) * _root;

            public void Bend(string name, Vector3 euler)
            {
                if (!_bones.TryGetValue(name, out Transform bone)) return;
                Quaternion delta = Delta(bone.parent) * Quaternion.Euler(euler);
                bone.rotation = _root * delta * Quaternion.Inverse(_root) * _rest[bone];
            }
        }
    }
}
