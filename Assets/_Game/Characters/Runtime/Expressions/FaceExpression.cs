using System;
using System.Collections.Generic;
using Game.Characters.Rng;
using UnityEngine;

namespace Game.Characters.Expressions
{
    /// <summary>
    /// How far each face feature moves from its resting place for an expression. It's symmetric, so one value covers
    /// both brows and both eyes; <see cref="FaceExpression"/> mirrors it per side.
    /// </summary>
    [Serializable]
    public struct FacePose
    {
        /// <summary>Metres up.</summary>
        public float BrowRaise;
        /// <summary>Degrees; positive raises the inner ends (worry, sadness), negative lowers them (anger).</summary>
        public float BrowTilt;
        /// <summary>Metres towards the middle of the face.</summary>
        public float BrowIn;
        /// <summary>Eye height change: -1 is shut, -0.3 a smiling squint, +0.2 wide.</summary>
        public float EyeOpen;
        /// <summary>Degrees of looking down.</summary>
        public float EyeLook;
        /// <summary>Relative mouth width change.</summary>
        public float MouthWidth;
        /// <summary>Relative mouth height change: +2 is an open smile or gasp.</summary>
        public float MouthOpen;
        /// <summary>Metres up (corners lifting reads as a smile even on a simple mouth).</summary>
        public float MouthRaise;
        /// <summary>Degrees the mouth's corners turn: positive curves up into a smile, negative down into a frown.</summary>
        public float MouthCurve;
        /// <summary>0..1 how flushed the cheeks are.</summary>
        public float Blush;
        /// <summary>0..1 eyes closed in a smile (^ ^): shuts the eyes and brings out the arcs over them.</summary>
        public float EyeSmile;

        public static FacePose Lerp(FacePose a, FacePose b, float t) => new FacePose
        {
            BrowRaise = Mathf.Lerp(a.BrowRaise, b.BrowRaise, t),
            BrowTilt = Mathf.Lerp(a.BrowTilt, b.BrowTilt, t),
            BrowIn = Mathf.Lerp(a.BrowIn, b.BrowIn, t),
            EyeOpen = Mathf.Lerp(a.EyeOpen, b.EyeOpen, t),
            EyeLook = Mathf.Lerp(a.EyeLook, b.EyeLook, t),
            MouthWidth = Mathf.Lerp(a.MouthWidth, b.MouthWidth, t),
            MouthOpen = Mathf.Lerp(a.MouthOpen, b.MouthOpen, t),
            MouthRaise = Mathf.Lerp(a.MouthRaise, b.MouthRaise, t),
            MouthCurve = Mathf.Lerp(a.MouthCurve, b.MouthCurve, t),
            Blush = Mathf.Lerp(a.Blush, b.Blush, t),
            EyeSmile = Mathf.Lerp(a.EyeSmile, b.EyeSmile, t),
        };
    }

    /// <summary>
    /// Facial expressions and blinking for a built character. Each frame it moves the face bones from the base the
    /// builder left them in: slider and style offsets, then expression on top. The builder resets every bone when it
    /// re-applies DNA and then calls <see cref="Rebase"/>, so expressions never compound with sliders. The stand-in
    /// rig's feature bones are the default; a real head that uses blendshapes would need its own driver.
    /// </summary>
    public sealed class FaceExpression : MonoBehaviour
    {
        public static readonly IReadOnlyDictionary<string, FacePose> Poses = new Dictionary<string, FacePose>
        {
            ["neutral"] = default,
            ["happy"] = new FacePose { BrowRaise = 0.002f, EyeOpen = -0.25f, MouthWidth = 0.25f, MouthOpen = 0.6f, MouthRaise = 0.001f, MouthCurve = 22f, Blush = 0.3f },
            ["delighted"] = new FacePose { BrowRaise = 0.004f, EyeOpen = -0.6f, MouthWidth = 0.35f, MouthOpen = 2.2f, MouthRaise = 0.0015f, MouthCurve = 26f, Blush = 0.6f, EyeSmile = 1f },
            ["shy"] = new FacePose { BrowRaise = 0.001f, BrowTilt = 18f, EyeOpen = -0.15f, EyeLook = 10f, MouthWidth = -0.3f, MouthCurve = 12f, Blush = 1f },
            ["sad"] = new FacePose { BrowRaise = -0.001f, BrowTilt = 28f, EyeOpen = -0.2f, EyeLook = 6f, MouthWidth = -0.2f, MouthOpen = -0.3f, MouthRaise = -0.0015f, MouthCurve = -24f },
            ["annoyed"] = new FacePose { BrowRaise = -0.003f, BrowTilt = -24f, BrowIn = 0.003f, EyeOpen = -0.3f, MouthWidth = -0.1f, MouthOpen = -0.4f, MouthCurve = -12f },
            ["surprised"] = new FacePose { BrowRaise = 0.006f, EyeOpen = 0.2f, MouthWidth = -0.35f, MouthOpen = 3f },
        };

        /// <summary>Off for deterministic captures.</summary>
        public static bool Blinking = true;

        public string EyeLeft = "Eye.L", EyeRight = "Eye.R";
        public string BrowLeft = "Brow.L", BrowRight = "Brow.R";
        public string Mouth = "Mouth";
        public string MouthLeft = "Mouth.L", MouthRight = "Mouth.R";
        public string BlushLeft = "Blush.L", BlushRight = "Blush.R";
        public string EyeSmileLeft = "EyeSmile.L", EyeSmileRight = "EyeSmile.R";

        /// <summary>How far smiling eyes push their arcs out, in metres.</summary>
        public float EyeSmileDepth = 0.0095f;

        /// <summary>How far a full blush pushes the lines out through the skin, in metres.</summary>
        public float BlushDepth = 0.0075f;

        /// <summary>How quickly the face moves towards its target, per second.</summary>
        public float Speed = 12f;

        struct Base
        {
            public Transform Bone;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        readonly List<Base> _bases = new List<Base>();
        Base? _eyeL, _eyeR, _browL, _browR, _mouth, _mouthL, _mouthR, _blushL, _blushR, _smileL, _smileR;
        bool _hasBase;
        FacePose _current, _target;
        string _expression = "neutral";
        float _holdUntil = float.PositiveInfinity;
        float _nextBlink = -1f, _blinkStart = -1f;
        const float BlinkSeconds = 0.12f;
        System.Random _random;

        public string Expression => _expression;

        /// <summary>Shows <paramref name="id"/> at <paramref name="strength"/> for <paramref name="hold"/> seconds, then relaxes.</summary>
        public void Show(string id, float strength = 1f, float hold = 2.5f)
        {
            if (!Poses.TryGetValue(id ?? "", out FacePose pose)) pose = default;
            _expression = id;
            _target = FacePose.Lerp(default, pose, Mathf.Clamp01(strength));
            _holdUntil = float.IsInfinity(hold) ? hold : Time.time + hold;
        }

        /// <summary>Jumps straight to the target expression (for screenshots).</summary>
        public void Snap()
        {
            _current = _target;
            LateUpdate();
        }

        /// <summary>The builder changed the bones underneath: take the new base before the next pose.</summary>
        public void Rebase() => _hasBase = false;

        void LateUpdate()
        {
            if (!_hasBase) Capture();
            if (_bases.Count == 0) return;

            if (Time.time >= _holdUntil)
            {
                _target = default;
                _expression = "neutral";
                _holdUntil = float.PositiveInfinity;
            }
            _current = FacePose.Lerp(_current, _target, 1f - Mathf.Exp(-Speed * Time.deltaTime));

            FacePose pose = _current;
            float blink = Blink();
            pose.EyeOpen = Mathf.Lerp(pose.EyeOpen, -0.92f, Mathf.Max(blink, pose.EyeSmile));

            Pose(_browL, new Vector3(pose.BrowIn, pose.BrowRaise, 0f), new Vector3(0f, 0f, pose.BrowTilt), Vector3.one);
            Pose(_browR, new Vector3(-pose.BrowIn, pose.BrowRaise, 0f), new Vector3(0f, 0f, -pose.BrowTilt), Vector3.one);
            var eyeScale = new Vector3(1f, Mathf.Max(0.05f, 1f + pose.EyeOpen), 1f);
            Pose(_eyeL, Vector3.zero, new Vector3(pose.EyeLook, 0f, 0f), eyeScale);
            Pose(_eyeR, Vector3.zero, new Vector3(pose.EyeLook, 0f, 0f), eyeScale);
            Pose(_mouth, new Vector3(0f, pose.MouthRaise, 0f), Vector3.zero,
                new Vector3(Mathf.Max(0.2f, 1f + pose.MouthWidth), Mathf.Max(0.2f, 1f + pose.MouthOpen), 1f));
            // The left corner's outer end is towards -x, so lifting it turns the other way from the right's. A narrow,
            // open mouth (surprise) tucks the corners away so the middle makes a round "o".
            float pucker = Mathf.Clamp01(-pose.MouthWidth / 0.35f) * Mathf.Clamp01(pose.MouthOpen / 3f);
            Vector3 corner = Vector3.one * Mathf.Lerp(1f, 0.3f, pucker);
            Pose(_mouthL, Vector3.zero, new Vector3(0f, 0f, -pose.MouthCurve), corner);
            Pose(_mouthR, Vector3.zero, new Vector3(0f, 0f, pose.MouthCurve), corner);
            var flush = new Vector3(0f, 0f, pose.Blush * BlushDepth);
            Pose(_blushL, flush, Vector3.zero, Vector3.one);
            Pose(_blushR, flush, Vector3.zero, Vector3.one);
            var arcs = new Vector3(0f, 0f, pose.EyeSmile * EyeSmileDepth);
            Pose(_smileL, arcs, Vector3.zero, Vector3.one);
            Pose(_smileR, arcs, Vector3.zero, Vector3.one);
        }

        static void Pose(Base? feature, Vector3 offset, Vector3 euler, Vector3 scale)
        {
            if (feature == null) return;
            Base b = feature.Value;
            b.Bone.localPosition = b.Position + offset;
            b.Bone.localRotation = b.Rotation * Quaternion.Euler(euler);
            b.Bone.localScale = Vector3.Scale(b.Scale, scale);
        }

        void Capture()
        {
            _bases.Clear();
            var bones = new Dictionary<string, Transform>();
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(bone.name)) bones[bone.name] = bone;

            Base? Take(string name)
            {
                if (string.IsNullOrEmpty(name) || !bones.TryGetValue(name, out Transform bone)) return null;
                var b = new Base { Bone = bone, Position = bone.localPosition, Rotation = bone.localRotation, Scale = bone.localScale };
                _bases.Add(b);
                return b;
            }

            _eyeL = Take(EyeLeft);
            _eyeR = Take(EyeRight);
            _browL = Take(BrowLeft);
            _browR = Take(BrowRight);
            _mouth = Take(Mouth);
            _mouthL = Take(MouthLeft);
            _mouthR = Take(MouthRight);
            _blushL = Take(BlushLeft);
            _blushR = Take(BlushRight);
            _smileL = Take(EyeSmileLeft);
            _smileR = Take(EyeSmileRight);
            _hasBase = true;
        }

        /// <summary>0..1 eyelid closure: a quick blink every few seconds, at times that differ per character.</summary>
        float Blink()
        {
            if (!Blinking) return 0f;
            _random ??= new System.Random((int)StableHash.Fnv1a64(name));
            float now = Time.time;
            if (_nextBlink < 0f) _nextBlink = now + 1f + 3f * (float)_random.NextDouble();
            if (now >= _nextBlink)
            {
                _blinkStart = now;
                _nextBlink = now + 2.5f + 3f * (float)_random.NextDouble();
            }
            float t = (now - _blinkStart) / BlinkSeconds;
            return _blinkStart < 0f || t >= 1f ? 0f : Mathf.Sin(Mathf.PI * t);
        }
    }
}
