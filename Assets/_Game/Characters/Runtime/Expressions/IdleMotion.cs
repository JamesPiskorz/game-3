using Game.Characters.Rng;
using UnityEngine;

namespace Game.Characters.Expressions
{
    /// <summary>
    /// Keeps a standing character alive without an animator: slow breathing in the chest, a slight sway from the
    /// spine (the hips stay put, so feet don't slide), a wandering gaze, and a nod on cue. Each character's rhythm
    /// is offset so a crowd doesn't move in unison. Like <see cref="FaceExpression"/>, it layers over whatever pose
    /// the bones were left in and is rebased by the builder after every Apply. A real animation rig would replace it.
    /// </summary>
    public sealed class IdleMotion : MonoBehaviour
    {
        public string Spine = "Spine", Chest = "Chest", Head = "Head";

        /// <summary>Degrees of chest pitch per breath, spine roll, and head turn.</summary>
        public float Breath = 1.2f, Sway = 1.4f, Look = 5f;

        public const float NodSeconds = 0.5f;
        const float NodDegrees = 8f;

        Transform _spine, _chest, _head;
        Quaternion _spineBase, _chestBase, _headBase;
        bool _hasBase;
        float _phase = -1f;
        float _nodStart = float.NegativeInfinity;

        public void Rebase() => _hasBase = false;

        /// <summary>A small nod, as when someone answers.</summary>
        public void Nod() => _nodStart = Time.time;

        void LateUpdate() => Pose(Time.time);

        /// <summary>Poses the body for time <paramref name="time"/> (seconds). Repeatable: the same time gives the same pose.</summary>
        public void Pose(float time)
        {
            if (!_hasBase) Capture();
            if (_phase < 0f) _phase = (StableHash.Fnv1a64(name) % 10000) / 1000f;
            float t = time + _phase;

            float breath = Mathf.Sin(t * 2f * Mathf.PI / 4.2f) * Breath;
            float sway = Mathf.Sin(t * 2f * Mathf.PI / 7.3f) * Sway;
            float look = (Mathf.Sin(t * 2f * Mathf.PI / 9.1f) + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / 3.7f)) / 1.5f * Look;
            float since = time - _nodStart;
            float nod = since >= 0f && since < NodSeconds ? Mathf.Sin(Mathf.PI * since / NodSeconds) * NodDegrees : 0f;

            if (_spine != null) _spine.localRotation = _spineBase * Quaternion.Euler(0f, 0f, sway);
            if (_chest != null) _chest.localRotation = _chestBase * Quaternion.Euler(breath, 0f, 0f);
            if (_head != null) _head.localRotation = _headBase * Quaternion.Euler(nod, look, 0f);
        }

        void Capture()
        {
            _spine = _chest = _head = null;
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                if (_spine == null && bone.name == Spine) _spine = bone;
                else if (_chest == null && bone.name == Chest) _chest = bone;
                else if (_head == null && bone.name == Head) _head = bone;
            }
            if (_spine != null) _spineBase = _spine.localRotation;
            if (_chest != null) _chestBase = _chest.localRotation;
            if (_head != null) _headBase = _head.localRotation;
            _hasBase = true;
        }
    }
}
