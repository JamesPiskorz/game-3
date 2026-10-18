using System.Collections.Generic;
using UnityEngine;

namespace Game.Characters.Rendering
{
    /// <summary>
    /// Where the toon shader samples the environment's shadow for this character (graphics plan R2-R3). A character
    /// never shadows itself, and is either in the environment's shadow or out of it: the shadow map is read once, at a
    /// point beside the head moved toward the light, and lights the whole character. Until this runs, materials
    /// keep their default anchor, which means "unshadowed" (right for the portrait studio and review sheets).
    /// </summary>
    public sealed class CharacterShadowAnchor : MonoBehaviour
    {
        /// <summary>How far toward the light the sample sits, so the character's own shadow can't cover it.</summary>
        public const float TowardLight = 0.6f;

        /// <summary>Height of the sample above the feet when there's no head bone.</summary>
        const float FallbackHeight = 1.5f;

        Transform _head;
        readonly List<Material> _materials = new List<Material>();
        bool _stale = true;
        Vector3 _last = new Vector3(float.NaN, 0f, 0f);

        /// <summary>Picks up new materials on the next update (call after parts change).</summary>
        public void Refresh() => _stale = true;

        /// <summary>The point the shadow is sampled at, before the shader moves it toward the light.</summary>
        public Vector3 Point => _head != null ? _head.position : transform.position + Vector3.up * FallbackHeight;

        void LateUpdate() => Apply();

        public void Apply()
        {
            if (_stale)
            {
                _stale = false;
                _materials.Clear();
                _head = FindDeep(transform, "Head");
                // Active renderers only: a part swapped out waits, inactive, for its end-of-frame destroy.
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null && material.HasVector(ToonMaterial.ShadowAnchor) && !_materials.Contains(material))
                            _materials.Add(material);
                _last = new Vector3(float.NaN, 0f, 0f);
            }
            Vector3 point = Point;
            if ((point - _last).sqrMagnitude < 1e-4f) return; // under a centimetre: nothing to update
            _last = point;
            foreach (Material material in _materials)
            {
                if (material == null) // destroyed with a part that went without a Refresh: pick up the new ones next frame
                {
                    _stale = true;
                    continue;
                }
                ToonMaterial.SetShadowAnchor(material, point, TowardLight);
            }
        }

        static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
