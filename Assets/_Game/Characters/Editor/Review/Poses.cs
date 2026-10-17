using System.Linq;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>Simple bone poses for review renders (stand-in skeleton names).</summary>
    public static class Poses
    {
        /// <summary>Arms down with a slight bend, so characters can stand close together in a lineup.</summary>
        public static void Relaxed(Transform character)
        {
            Rotate(character, "UpperArm.L", new Vector3(0f, 8f, 72f));
            Rotate(character, "UpperArm.R", new Vector3(0f, -8f, -72f));
            Rotate(character, "LowerArm.L", new Vector3(0f, 12f, 0f));
            Rotate(character, "LowerArm.R", new Vector3(0f, -12f, 0f));
        }

        static void Rotate(Transform character, string bone, Vector3 euler)
        {
            Transform transform = character.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == bone);
            if (transform != null) transform.localRotation = Quaternion.Euler(euler);
        }
    }
}
