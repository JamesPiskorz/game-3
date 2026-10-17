using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game.Characters.StandIns
{
    public readonly struct StandInBone
    {
        public readonly string Name;
        public readonly string Parent;
        public readonly Vector3 Position;

        public StandInBone(string name, string parent, Vector3 position)
        {
            Name = name;
            Parent = parent;
            Position = position;
        }
    }

    /// <summary>
    /// The stand-in rest skeleton: T-pose facing +Z, character's left at -X, metres, identity bone rotations.
    /// Face bones let bone-driven face sliders move the stand-in's eyes, brows, nose and mouth.
    /// Parents are listed before children.
    /// </summary>
    public static class StandInSkeleton
    {
        public static readonly Vector3 HeadCentre = new Vector3(0f, 1.64f, 0f);
        public const float HeadRadius = 0.11f;

        public static readonly StandInBone[] Bones =
        {
            new StandInBone("Hips", null, new Vector3(0f, 0.95f, 0f)),
            new StandInBone("Spine", "Hips", new Vector3(0f, 1.07f, 0f)),
            new StandInBone("Chest", "Spine", new Vector3(0f, 1.24f, 0f)),
            new StandInBone("Neck", "Chest", new Vector3(0f, 1.44f, 0f)),
            new StandInBone("Head", "Neck", new Vector3(0f, 1.54f, 0f)),
            new StandInBone("Eye.L", "Head", new Vector3(-0.04f, 1.655f, 0.097f)),
            new StandInBone("Eye.R", "Head", new Vector3(0.04f, 1.655f, 0.097f)),
            new StandInBone("Brow.L", "Head", new Vector3(-0.042f, 1.695f, 0.087f)),
            new StandInBone("Brow.R", "Head", new Vector3(0.042f, 1.695f, 0.087f)),
            new StandInBone("Nose", "Head", new Vector3(0f, 1.625f, 0.106f)),
            new StandInBone("Mouth", "Head", new Vector3(0f, 1.59f, 0.1f)),
            // The mouth's corners pivot at the inner ends of the side pieces, so turning them curves the mouth.
            new StandInBone("Mouth.L", "Mouth", new Vector3(-0.008f, 1.59f, 0.1f)),
            new StandInBone("Mouth.R", "Mouth", new Vector3(0.008f, 1.59f, 0.1f)),
            new StandInBone("UpperArm.L", "Chest", new Vector3(-0.16f, 1.4f, 0f)),
            new StandInBone("LowerArm.L", "UpperArm.L", new Vector3(-0.44f, 1.4f, 0f)),
            new StandInBone("Hand.L", "LowerArm.L", new Vector3(-0.69f, 1.4f, 0f)),
            new StandInBone("UpperArm.R", "Chest", new Vector3(0.16f, 1.4f, 0f)),
            new StandInBone("LowerArm.R", "UpperArm.R", new Vector3(0.44f, 1.4f, 0f)),
            new StandInBone("Hand.R", "LowerArm.R", new Vector3(0.69f, 1.4f, 0f)),
            new StandInBone("UpperLeg.L", "Hips", new Vector3(-0.09f, 0.9f, 0f)),
            new StandInBone("LowerLeg.L", "UpperLeg.L", new Vector3(-0.09f, 0.5f, 0f)),
            new StandInBone("Foot.L", "LowerLeg.L", new Vector3(-0.09f, 0.09f, 0f)),
            new StandInBone("UpperLeg.R", "Hips", new Vector3(0.09f, 0.9f, 0f)),
            new StandInBone("LowerLeg.R", "UpperLeg.R", new Vector3(0.09f, 0.5f, 0f)),
            new StandInBone("Foot.R", "LowerLeg.R", new Vector3(0.09f, 0.09f, 0f)),
        };

        /// <summary>A chain of part-only bones (hair tails), hanging from <paramref name="parent"/>; grafted on equip.</summary>
        public static StandInBone[] Chain(string prefix, string parent, Vector3 start, Vector3 end, int count)
        {
            var bones = new StandInBone[count];
            for (int i = 0; i < count; i++)
                bones[i] = new StandInBone($"{prefix}.{i + 1}", i == 0 ? parent : $"{prefix}.{i}", Vector3.Lerp(start, end, i / (float)count));
            return bones;
        }

        public static StandInBone[] With(params StandInBone[][] extras) => Bones.Concat(extras.SelectMany(e => e)).ToArray();

        public static Vector3 Position(IEnumerable<StandInBone> bones, string name) => bones.First(b => b.Name == name).Position;

        /// <summary>Builds the bone hierarchy under an "Armature" child of <paramref name="parent"/> (assumed at the origin).</summary>
        public static Transform Build(Transform parent, IEnumerable<StandInBone> bones, Dictionary<string, Transform> map)
        {
            var armature = new GameObject("Armature").transform;
            armature.SetParent(parent, false);
            foreach (StandInBone bone in bones)
            {
                var transform = new GameObject(bone.Name).transform;
                transform.SetParent(bone.Parent == null ? armature : map[bone.Parent], false);
                transform.position = bone.Position;
                map[bone.Name] = transform;
            }
            return armature;
        }
    }
}
