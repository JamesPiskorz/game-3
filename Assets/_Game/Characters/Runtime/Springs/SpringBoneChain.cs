using System.Collections.Generic;
using UnityEngine;

namespace Game.Characters.Springs
{
    /// <summary>
    /// Verlet spring bones for one chain (hair tail, skirt panel): each bone's tail is a point mass pulled back
    /// towards its rest direction, damped, weighted by gravity, held at the bone's length and pushed out of colliders.
    /// The same model as VRM's SpringBone, small enough to own. Steps at a fixed rate so the motion doesn't depend
    /// on frame rate.
    /// </summary>
    public sealed class SpringBoneChain : MonoBehaviour
    {
        const float StepSeconds = 1f / 90f;
        const int MaxStepsPerFrame = 6;
        // A bigger jump than this between steps is a teleport (spawn, location change), not motion to react to.
        const float TeleportDistance = 0.5f;

        [Tooltip("First bone of the chain; its parent is the anchor that moves it.")]
        public Transform Root;
        [Range(0f, 8f)] public float Stiffness = 1.6f;
        [Range(0f, 1f)] public float Drag = 0.35f;
        [Tooltip("Pull per second on each tail, in the same units as stiffness (VRM's gravity power × direction).")]
        public Vector3 Gravity = new Vector3(0f, -0.5f, 0f);
        [Tooltip("Radius of each joint's tail, for collisions.")]
        public float HitRadius = 0.02f;
        public List<SpringCollider> Colliders = new List<SpringCollider>();

        struct Joint
        {
            public Transform Bone;
            public Quaternion RestLocalRotation;
            public Vector3 Axis;      // tail direction in the bone's local space at rest
            public float Length;      // bone-to-tail distance at rest (local units)
            public Vector3 Tail, PreviousTail;
        }

        readonly List<Joint> _joints = new List<Joint>();
        float _accumulated;
        Vector3 _lastAnchor;

        public bool IsInitialized => _joints.Count > 0;

        /// <summary>Captures the chain's rest pose. Call with the chain at rest.</summary>
        public void Initialize()
        {
            _joints.Clear();
            for (Transform bone = Root; bone != null; bone = bone.childCount > 0 ? bone.GetChild(0) : null)
            {
                Vector3 localTail;
                if (bone.childCount > 0) localTail = bone.GetChild(0).localPosition;
                else
                {
                    // The last bone has no child: extend it the length of the previous segment.
                    Vector3 fromParent = bone.localPosition;
                    localTail = Quaternion.Inverse(bone.localRotation) * fromParent;
                }
                if (localTail.sqrMagnitude < 1e-10f) break;

                Vector3 tail = bone.TransformPoint(localTail);
                _joints.Add(new Joint
                {
                    Bone = bone,
                    RestLocalRotation = bone.localRotation,
                    Axis = localTail.normalized,
                    Length = localTail.magnitude,
                    Tail = tail,
                    PreviousTail = tail,
                });
            }
            _lastAnchor = AnchorPosition;
        }

        Vector3 AnchorPosition => Root != null && Root.parent != null ? Root.parent.position : Vector3.zero;

        /// <summary>Snaps every tail to its rest position (after teleports or rebuilds), cancelling motion.</summary>
        public void ResetToRest()
        {
            for (int i = 0; i < _joints.Count; i++)
            {
                Joint joint = _joints[i];
                joint.Bone.localRotation = joint.RestLocalRotation;
                joint.Tail = joint.PreviousTail = joint.Bone.TransformPoint(joint.Axis * joint.Length);
                _joints[i] = joint;
            }
        }

        void LateUpdate()
        {
            if (!IsInitialized) Initialize();
            Advance(Time.deltaTime);
        }

        /// <summary>Advances the simulation by <paramref name="seconds"/> in fixed steps.</summary>
        public void Advance(float seconds)
        {
            if (!IsInitialized) return;
            if ((AnchorPosition - _lastAnchor).sqrMagnitude > TeleportDistance * TeleportDistance) ResetToRest();
            _lastAnchor = AnchorPosition;
            _accumulated += seconds;
            int steps = 0;
            while (_accumulated >= StepSeconds && steps < MaxStepsPerFrame)
            {
                Step(StepSeconds);
                _accumulated -= StepSeconds;
                steps++;
            }
            if (steps == MaxStepsPerFrame) _accumulated = 0f; // a long hitch: drop the backlog rather than spiral
        }

        void Step(float dt)
        {
            for (int i = 0; i < _joints.Count; i++)
            {
                Joint joint = _joints[i];
                Transform bone = joint.Bone;
                if (bone == null) continue;

                Quaternion parentRotation = bone.parent != null ? bone.parent.rotation : Quaternion.identity;
                Quaternion restRotation = parentRotation * joint.RestLocalRotation;
                Vector3 head = bone.position;
                float length = joint.Length * bone.lossyScale.x;

                Vector3 restDirection = restRotation * joint.Axis;
                Vector3 inertia = (joint.Tail - joint.PreviousTail) * (1f - Drag);
                Vector3 next = joint.Tail + inertia + (restDirection * Stiffness + Gravity) * dt;

                next = head + (next - head).normalized * length;
                // Twice: restoring the bone length after one push can nudge the tail back into another collider.
                for (int pass = 0; pass < 2; pass++)
                    foreach (SpringCollider collider in Colliders)
                        if (collider != null) next = collider.PushOut(next, HitRadius, head, length);

                joint.PreviousTail = joint.Tail;
                joint.Tail = next;
                bone.rotation = Quaternion.FromToRotation(restDirection, next - head) * restRotation;
                _joints[i] = joint;
            }
        }

        /// <summary>World-space tail positions, root first (for tests and debugging).</summary>
        public IEnumerable<Vector3> Tails()
        {
            foreach (Joint joint in _joints) yield return joint.Tail;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            foreach (Joint joint in _joints)
                if (joint.Bone != null) Gizmos.DrawLine(joint.Bone.position, joint.Tail);
        }
    }
}
