using System.Collections.Generic;
using Game.Characters.Building;
using Game.Characters.Rng;
using UnityEngine;

namespace Game.Characters.Expressions
{
    /// <summary>
    /// Keeps a character alive without an animator. Standing, it breathes, sways a little from the spine (the hips
    /// stay put, so feet don't slide), lets its gaze wander and nods on cue. Moving, it blends into a simple walk
    /// cycle: swinging legs, bending knees, counter-swinging arms and a slight bob. On cue it waves its right hand.
    /// Each character's rhythm is offset so a crowd doesn't move in unison. Like <see cref="FaceExpression"/>, it
    /// layers over whatever pose the bones were left in and is rebased by the builder after every Apply. A real
    /// animation rig would replace it.
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

        /// <summary>The waving arm's bones (the right arm: the wave's directions are on the character's right).</summary>
        public string WaveUpperArm = "UpperArm.R", WaveLowerArm = "LowerArm.R", WaveHand = "Hand.R";

        public const float NodSeconds = 0.5f;
        const float NodDegrees = 8f;

        /// <summary>How long a wave lasts, raising and lowering the arm included; the hand goes side to side WaveRate times a second.</summary>
        public const float WaveSeconds = 2.6f;
        const float WaveRate = 1.8f;

        static readonly string[] Limbs = { "UpperLeg.L", "UpperLeg.R", "LowerLeg.L", "LowerLeg.R", "UpperArm.L", "UpperArm.R" };

        Transform _hips, _spine, _chest, _head;
        Vector3 _hipsBase;
        Quaternion _spineBase, _chestBase, _headBase;
        readonly Transform[] _limbs = new Transform[6];
        readonly Quaternion[] _limbBase = new Quaternion[6];
        bool _hasBase;
        float _phase = -1f;
        float _nodStart = float.NegativeInfinity;
        float _waveStart = float.NegativeInfinity;
        float _walk, _sit;
        Transform _waveUpper, _waveLower, _waveHand;
        Quaternion _waveUpperBase, _waveLowerBase, _waveHandBase;
        Quaternion _restUpper, _restLower, _restHand; // T-pose rotations, in the character's frame
        Vector3 _restUpperDirection, _restLowerDirection;
        bool _canWave;

        /// <summary>How much the character is walking, 0 (standing) to 1; set by whatever moves them. Eased in and out.</summary>
        public float Walking { get; set; }

        /// <summary>How much they're sitting, 0 to 1 (on a seat at hip height of about 0.5 m). Eased in and out.</summary>
        public float Sitting { get; set; }

        /// <summary>Jumps the sitting blend to <see cref="Sitting"/> (for screenshots and tests).</summary>
        public void SnapSitting() => _sit = Sitting;

        public void Rebase() => _hasBase = false;

        /// <summary>A small nod, as when someone answers.</summary>
        public void Nod() => _nodStart = Time.time;

        /// <summary>
        /// A wave of the right hand, lasting <see cref="WaveSeconds"/>: from now, or from <paramref name="start"/> in
        /// <see cref="Pose"/>'s time (for captures that step time themselves).
        /// </summary>
        public void Wave(float start = float.NaN) => _waveStart = float.IsNaN(start) ? Time.time : start;

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
            if (_canWave)
            {
                _waveUpper.localRotation = _waveUpperBase;
                _waveLower.localRotation = _waveLowerBase;
                _waveHand.localRotation = _waveHandBase;
            }
            // Sitting folds the legs at hip and knee and lowers the hips onto the seat; the hands come forward.
            Limb(0, -LegSwing * swing - SitHip * _sit);
            Limb(1, LegSwing * swing - SitHip * _sit);
            Limb(2, KneeBend * Mathf.Max(0f, Mathf.Sin(stride + Mathf.PI / 2f)) * _walk + SitKnee * _sit);
            Limb(3, KneeBend * Mathf.Max(0f, Mathf.Sin(stride - Mathf.PI / 2f)) * _walk + SitKnee * _sit);
            Limb(4, ArmSwing * swing - SitArms * _sit);
            Limb(5, -ArmSwing * swing - SitArms * _sit);
            if (_hips != null) _hips.localPosition = _hipsBase + Vector3.up * (Bob * Mathf.Abs(Mathf.Cos(stride)) * _walk - SitDrop * _sit);

            float waving = time - _waveStart;
            if (_canWave && waving >= 0f && waving < WaveSeconds) PoseWave(waving);
        }

        // The arm comes up and out, the forearm upright with the palm forward, and the hand goes side to side from the
        // elbow, the wrist trailing a little; then it all goes back. Directions are in the character's frame (+X its
        // right, +Y up, +Z forward), and each bone is turned from its T-pose, so any rig's bone axes do the same.
        void PoseWave(float since)
        {
            float weight = Mathf.Min(Mathf.SmoothStep(0f, 1f, since / 0.4f), Mathf.SmoothStep(0f, 1f, (WaveSeconds - since) / 0.5f));
            float settle = Mathf.SmoothStep(0f, 1f, (since - 0.2f) / 0.4f);
            float side = Mathf.Sin(2f * Mathf.PI * WaveRate * Mathf.Max(0f, since - 0.3f)) * settle;
            float wrist = Mathf.Sin(2f * Mathf.PI * WaveRate * Mathf.Max(0f, since - 0.38f)) * settle;

            Vector3 upper = new Vector3(0.82f, 0.38f, 0.3f).normalized;
            Vector3 lower = Quaternion.AngleAxis(-24f * side, Vector3.forward) * new Vector3(0.12f, 1f, 0.16f).normalized;
            Vector3 hand = Quaternion.AngleAxis(-16f * wrist, Vector3.forward) * lower;

            Quaternion root = transform.rotation;
            Quaternion fromUpper = _waveUpper.rotation, fromLower = _waveLower.rotation, fromHand = _waveHand.rotation;
            _waveUpper.rotation = Quaternion.Slerp(fromUpper, root * Aim(_restUpperDirection, upper, 0f) * _restUpper, weight);
            _waveLower.rotation = Quaternion.Slerp(fromLower, root * Aim(_restLowerDirection, lower, 0.5f) * _restLower, weight);
            _waveHand.rotation = Quaternion.Slerp(fromHand, root * Aim(_restLowerDirection, hand, 1f) * _restHand, weight);
        }

        // Turns a bone from its T-pose direction to `direction`, then about that direction by `twist` of the turn that
        // brings the palm (down in the T-pose) to face forward: the forearm takes half, the hand all of it.
        static Quaternion Aim(Vector3 restDirection, Vector3 direction, float twist)
        {
            Quaternion turn = Quaternion.FromToRotation(restDirection, direction);
            Vector3 palm = Vector3.ProjectOnPlane(turn * Vector3.down, direction);
            Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, direction);
            return Quaternion.AngleAxis(twist * Vector3.SignedAngle(palm, forward, direction), direction) * turn;
        }

        // A bone's T-pose position and rotation in the character's frame, from the build-time rest pose.
        void RestInCharacter(RestPose rest, Transform bone, out Vector3 position, out Quaternion rotation)
        {
            var chain = new List<Transform>();
            for (Transform b = bone; b != null && b != transform; b = b.parent) chain.Add(b);
            position = Vector3.zero;
            rotation = Quaternion.identity;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                Transform b = chain[i];
                bool known = rest.Find(b.name) == b;
                position += rotation * (known ? rest.Of(b).Position : b.localPosition);
                rotation *= known ? rest.Of(b).Rotation : b.localRotation;
            }
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

            _waveUpper = _waveLower = _waveHand = null;
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                if (_waveUpper == null && bone.name == WaveUpperArm) _waveUpper = bone;
                else if (_waveLower == null && bone.name == WaveLowerArm) _waveLower = bone;
                else if (_waveHand == null && bone.name == WaveHand) _waveHand = bone;
            }
            RestPose rest = GetComponent<RestPose>();
            _canWave = _waveUpper != null && _waveLower != null && _waveHand != null && rest != null;
            if (_canWave)
            {
                _waveUpperBase = _waveUpper.localRotation;
                _waveLowerBase = _waveLower.localRotation;
                _waveHandBase = _waveHand.localRotation;
                RestInCharacter(rest, _waveUpper, out Vector3 shoulder, out _restUpper);
                RestInCharacter(rest, _waveLower, out Vector3 elbow, out _restLower);
                RestInCharacter(rest, _waveHand, out Vector3 wrist, out _restHand);
                _restUpperDirection = elbow - shoulder;
                _restLowerDirection = wrist - elbow;
            }
            _hasBase = true;
        }
    }
}
