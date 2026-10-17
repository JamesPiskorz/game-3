using Game.Characters.Rng;
using UnityEngine;

namespace Game.Characters.Expressions
{
    /// <summary>
    /// Keeps a character alive without an animator. Standing, it breathes, sways a little from the spine (the hips
    /// stay put, so feet don't slide), lets its gaze wander and nods on cue. Moving, it blends into a simple walk
    /// cycle: swinging legs, bending knees, counter-swinging arms and a slight bob. Each character's rhythm is offset
    /// so a crowd doesn't move in unison. Like <see cref="FaceExpression"/>, it layers over whatever pose the bones
    /// were left in and is rebased by the builder after every Apply. A real animation rig would replace it.
    /// </summary>
    public sealed class IdleMotion : MonoBehaviour
    {
        public string Hips = "Hips", Spine = "Spine", Chest = "Chest", Head = "Head";

        /// <summary>Degrees of chest pitch per breath, spine roll, and head turn.</summary>
        public float Breath = 1.2f, Sway = 1.4f, Look = 5f;

        /// <summary>Walk cycles per second, and the leg and arm swing in degrees.</summary>
        public float StepRate = 0.9f, LegSwing = 24f, KneeBend = 32f, ArmSwing = 16f, Bob = 0.018f;

        /// <summary>Sitting: how far the hips drop (metres), how far the thighs come forward and the knees bend, and the hands come to the lap (degrees).</summary>
        public float SitDrop = 0.42f, SitHip = 88f, SitKnee = 88f, SitArms = 28f;

        public const float NodSeconds = 0.5f;
        const float NodDegrees = 8f;

        static readonly string[] Limbs = { "UpperLeg.L", "UpperLeg.R", "LowerLeg.L", "LowerLeg.R", "UpperArm.L", "UpperArm.R" };

        Transform _hips, _spine, _chest, _head;
        Vector3 _hipsBase;
        Quaternion _spineBase, _chestBase, _headBase;
        readonly Transform[] _limbs = new Transform[6];
        readonly Quaternion[] _limbBase = new Quaternion[6];
        bool _hasBase;
        float _phase = -1f;
        float _nodStart = float.NegativeInfinity;
        float _walk, _sit;

        /// <summary>How much the character is walking, 0 (standing) to 1; set by whatever moves them. Eased in and out.</summary>
        public float Walking { get; set; }

        /// <summary>How much they're sitting, 0 to 1 (on a seat at hip height of about 0.5 m). Eased in and out.</summary>
        public float Sitting { get; set; }

        /// <summary>Jumps the sitting blend to <see cref="Sitting"/> (for screenshots and tests).</summary>
        public void SnapSitting() => _sit = Sitting;

        public void Rebase() => _hasBase = false;

        /// <summary>A small nod, as when someone answers.</summary>
        public void Nod() => _nodStart = Time.time;

        /// <summary>Jumps the walk blend to <see cref="Walking"/> (for screenshots and tests).</summary>
        public void SnapWalking() => _walk = Walking;

        void LateUpdate()
        {
            _walk = Mathf.MoveTowards(_walk, Walking, Time.deltaTime * 4f);
            _sit = Mathf.MoveTowards(_sit, Sitting, Time.deltaTime * 2.5f);
            Pose(Time.time);
        }

        /// <summary>Poses the body for time <paramref name="time"/> (seconds). Repeatable: the same time gives the same pose.</summary>
        public void Pose(float time)
        {
            if (!_hasBase) Capture();
            if (_phase < 0f) _phase = (StableHash.Fnv1a64(name) % 10000) / 1000f;
            float t = time + _phase;
            float still = 1f - _walk;

            float breath = Mathf.Sin(t * 2f * Mathf.PI / 4.2f) * Breath;
            float sway = Mathf.Sin(t * 2f * Mathf.PI / 7.3f) * Sway * still;
            float look = (Mathf.Sin(t * 2f * Mathf.PI / 9.1f) + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / 3.7f)) / 1.5f * Look * still;
            float since = time - _nodStart;
            float nod = since >= 0f && since < NodSeconds ? Mathf.Sin(Mathf.PI * since / NodSeconds) * NodDegrees : 0f;

            if (_spine != null) _spine.localRotation = _spineBase * Quaternion.Euler(0f, 0f, sway);
            if (_chest != null) _chest.localRotation = _chestBase * Quaternion.Euler(breath, 0f, 0f);
            if (_head != null) _head.localRotation = _headBase * Quaternion.Euler(nod, look, 0f);

            // The walk: rotations about the character's sideways axis, in each limb's parent space (rest rotations
            // are identity, so that's the parent's X axis). Negative swings the limb forward.
            float stride = t * 2f * Mathf.PI * StepRate;
            float swing = Mathf.Sin(stride) * _walk;
            // Sitting folds the legs at hip and knee and lowers the hips onto the seat; the hands come forward.
            Limb(0, -LegSwing * swing - SitHip * _sit);
            Limb(1, LegSwing * swing - SitHip * _sit);
            Limb(2, KneeBend * Mathf.Max(0f, Mathf.Sin(stride + Mathf.PI / 2f)) * _walk + SitKnee * _sit);
            Limb(3, KneeBend * Mathf.Max(0f, Mathf.Sin(stride - Mathf.PI / 2f)) * _walk + SitKnee * _sit);
            Limb(4, ArmSwing * swing - SitArms * _sit);
            Limb(5, -ArmSwing * swing - SitArms * _sit);
            if (_hips != null) _hips.localPosition = _hipsBase + Vector3.up * (Bob * Mathf.Abs(Mathf.Cos(stride)) * _walk - SitDrop * _sit);
        }

        void Limb(int i, float degrees)
        {
            if (_limbs[i] != null) _limbs[i].localRotation = Quaternion.AngleAxis(degrees, Vector3.right) * _limbBase[i];
        }

        void Capture()
        {
            _hips = _spine = _chest = _head = null;
            for (int i = 0; i < _limbs.Length; i++) _limbs[i] = null;
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                if (_hips == null && bone.name == Hips) _hips = bone;
                else if (_spine == null && bone.name == Spine) _spine = bone;
                else if (_chest == null && bone.name == Chest) _chest = bone;
                else if (_head == null && bone.name == Head) _head = bone;
                else
                {
                    int limb = System.Array.IndexOf(Limbs, bone.name);
                    if (limb >= 0 && _limbs[limb] == null) _limbs[limb] = bone;
                }
            }
            if (_hips != null) _hipsBase = _hips.localPosition;
            if (_spine != null) _spineBase = _spine.localRotation;
            if (_chest != null) _chestBase = _chest.localRotation;
            if (_head != null) _headBase = _head.localRotation;
            for (int i = 0; i < _limbs.Length; i++)
                if (_limbs[i] != null) _limbBase[i] = _limbs[i].localRotation;
            _hasBase = true;
        }
    }
}
