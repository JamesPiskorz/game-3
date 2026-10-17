using Game.Characters.Springs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Characters.Rendering
{
    /// <summary>
    /// Distance-based detail for crowds. Far characters drop their outline pass (half the town's render cost in the
    /// benchmark, and sub-pixel at that range anyway), then spring bones, then shadows. Thresholds have a little
    /// hysteresis so characters at the boundary don't flicker.
    /// </summary>
    public sealed class CharacterLod : MonoBehaviour
    {
        const string OutlinePass = "SRPDefaultUnlit";
        const float Hysteresis = 1f;

        public float OutlineDistance = 14f;

        /// <summary>Small parts (<see cref="CharacterPart.Small"/>) keep their outline only this close, and never cast shadows.</summary>
        public float SmallOutlineDistance = 5f;
        public float SpringDistance = 16f;
        public float ShadowDistance = 24f;
        [Tooltip("Camera to measure from; empty uses Camera.main.")]
        public Camera Camera;

        bool _initialised, _outlines, _smallOutlines, _springs, _shadows;

        public bool Outlines => _outlines;
        public bool Springs => _springs;
        public bool Shadows => _shadows;

        void LateUpdate()
        {
            Camera camera = Camera != null ? Camera : Camera.main;
            if (camera != null) Apply(Vector3.Distance(camera.transform.position, transform.position));
        }

        /// <summary>Forces the next <see cref="Apply"/> to touch every renderer (call after parts change).</summary>
        public void Refresh() => _initialised = false;

        public void Apply(float distance)
        {
            bool outlines = Within(distance, OutlineDistance, _outlines);
            bool smallOutlines = Within(distance, SmallOutlineDistance, _smallOutlines);
            bool springs = Within(distance, SpringDistance, _springs);
            bool shadows = Within(distance, ShadowDistance, _shadows);
            if (_initialised && outlines == _outlines && smallOutlines == _smallOutlines && springs == _springs && shadows == _shadows) return;

            _initialised = true;
            _outlines = outlines;
            _smallOutlines = smallOutlines;
            _springs = springs;
            _shadows = shadows;
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                // Tiny details don't need a shadow or, beyond a close-up, an outline: each is a draw for every pass.
                bool small = renderer.GetComponentInParent<Modular.CharacterPart>()?.Small == true;
                renderer.shadowCastingMode = shadows && !small ? ShadowCastingMode.On : ShadowCastingMode.Off;
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null) material.SetShaderPassEnabled(OutlinePass, small ? outlines && smallOutlines : outlines);
            }
            foreach (SpringBoneChain chain in GetComponents<SpringBoneChain>())
                chain.enabled = springs;
        }

        // Switch off past the threshold, back on only once clearly inside it.
        bool Within(float distance, float threshold, bool current) =>
            !_initialised ? distance < threshold : current ? distance < threshold + Hysteresis : distance < threshold - Hysteresis;
    }
}
