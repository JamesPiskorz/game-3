using System.Collections.Generic;
using UnityEngine;

namespace Game.Characters.Building
{
    /// <summary>
    /// The character's skeleton rest pose, captured once at build time, so slider and style bone offsets are always
    /// applied to the same base and re-applying DNA never compounds them. Bones grafted later (hair tails) aren't
    /// tracked: sliders don't drive them.
    /// </summary>
    public sealed class RestPose : MonoBehaviour
    {
        public struct Pose
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        readonly Dictionary<Transform, Pose> _poses = new Dictionary<Transform, Pose>();

        public void Capture()
        {
            _bones.Clear();
            _poses.Clear();
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                if (bone == transform || _bones.ContainsKey(bone.name)) continue;
                _bones[bone.name] = bone;
                _poses[bone] = new Pose { Position = bone.localPosition, Rotation = bone.localRotation, Scale = bone.localScale };
            }
        }

        public Transform Find(string name) => _bones.TryGetValue(name, out Transform bone) && bone != null ? bone : null;

        public Pose Of(Transform bone) => _poses[bone];

        /// <summary>Puts every captured bone back to rest (an animation pose on top is the animator's job).</summary>
        public void Restore()
        {
            foreach (KeyValuePair<Transform, Pose> pair in _poses)
            {
                if (pair.Key == null) continue;
                pair.Key.localPosition = pair.Value.Position;
                pair.Key.localRotation = pair.Value.Rotation;
                pair.Key.localScale = pair.Value.Scale;
            }
        }
    }
}
