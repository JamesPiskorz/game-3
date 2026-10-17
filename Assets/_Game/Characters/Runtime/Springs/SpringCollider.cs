using UnityEngine;

namespace Game.Characters.Springs
{
    /// <summary>A sphere spring-bone tails can't enter (head, neck, shoulders, chest).</summary>
    public sealed class SpringCollider : MonoBehaviour
    {
        public Vector3 Offset;
        public float Radius = 0.1f;

        public Vector3 Centre => transform.TransformPoint(Offset);
        public float WorldRadius => Radius * transform.lossyScale.x;

        /// <summary>Moves a tail point out of the sphere, then back onto the bone's length from its head.</summary>
        public Vector3 PushOut(Vector3 tail, float tailRadius, Vector3 head, float length)
        {
            Vector3 centre = Centre;
            float minimum = WorldRadius + tailRadius;
            Vector3 offset = tail - centre;
            if (offset.sqrMagnitude >= minimum * minimum) return tail;
            // A joint rooted inside the sphere (hair growing from the scalp) can't be pushed out of it sensibly.
            if ((head - centre).sqrMagnitude < WorldRadius * WorldRadius) return tail;

            Vector3 pushed = centre + offset.normalized * minimum;
            return head + (pushed - head).normalized * length;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.8f, 0.6f);
            Gizmos.DrawWireSphere(Centre, WorldRadius);
        }
    }
}
