using System;
using System.Linq;
using Game.Characters.Dna;
using Game.Characters.Expressions;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using Game.Characters.Springs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Characters.Building
{
    /// <summary>
    /// Renders characters into textures for UI: a hidden sitter in a studio far below the scene, re-dressed for each
    /// shot and rendered once by a camera that's otherwise off. With a sun given, it's swapped for neutral daylight for
    /// the shot, so pictures look the same whatever the scene's lighting (night in town, say).
    /// </summary>
    public sealed class PortraitStudio : IDisposable
    {
        public enum Framing
        {
            /// <summary>Head and shoulders.</summary>
            Face,

            /// <summary>The whole character.</summary>
            Body,
        }

        static readonly Vector3 StudioPosition = new Vector3(0f, -500f, 0f);

        readonly CharacterBuilder _builder;
        readonly Light _sun;
        readonly GameObject _studio;
        readonly Camera _camera;
        ModularCharacter _sitter;

        public PortraitStudio(CharacterBuilder builder, Color backdrop, Light sun = null)
        {
            _builder = builder;
            _sun = sun;
            _studio = new GameObject("Portrait studio") { hideFlags = HideFlags.DontSave };
            _studio.transform.position = StudioPosition;
            _camera = new GameObject("Portrait camera").AddComponent<Camera>();
            _camera.transform.SetParent(_studio.transform, false);
            _camera.enabled = false; // rendered by hand
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = backdrop;
            _camera.fieldOfView = 18f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 12f;
        }

        public static RenderTexture Target(int width, int height) =>
            new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4, hideFlags = HideFlags.DontSave };

        /// <summary>Renders <paramref name="dna"/> dressed for <paramref name="outfit"/> into <paramref name="target"/>.</summary>
        public void Render(CharacterDna dna, string outfit, RenderTexture target, Framing framing, string expression = "happy")
        {
            if (_sitter == null)
            {
                _sitter = _builder.Build(dna, outfit, _studio.transform);
                _sitter.name = "Sitter";
                if (_sitter.GetComponent<IdleMotion>() is IdleMotion idle) idle.enabled = false; // a still pose
            }
            else
            {
                _builder.Apply(_sitter, dna, outfit);
            }
            foreach (SpringBoneChain chain in _sitter.GetComponents<SpringBoneChain>()) chain.enabled = false;
            // Several shots can be taken in one frame: skin for each render, not once per frame, or new parts show stale.
            foreach (SkinnedMeshRenderer renderer in _sitter.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.forceMatrixRecalculationPerRender = true;
            _sitter.GetComponent<CharacterLod>()?.Apply(0f);
            Poses.Relaxed(_sitter.transform);
            if (_sitter.GetComponent<FaceExpression>() is FaceExpression face)
            {
                face.Show(expression, 0.45f, float.PositiveInfinity);
                face.Snap();
            }

            Transform head = _sitter.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
            if (framing == Framing.Face)
            {
                // From a little below eye level, the head with some room above for big hair.
                Vector3 look = head.position + new Vector3(0f, 0.1f, 0f);
                _camera.transform.position = look + new Vector3(0f, -0.02f, 1.12f);
                _camera.transform.LookAt(look - new Vector3(0f, 0.02f, 0f));
            }
            else
            {
                // Feet to the top of the hair, whatever their height.
                float top = head.position.y + 0.3f, bottom = _sitter.transform.position.y - 0.04f;
                Vector3 middle = new Vector3(_sitter.transform.position.x, (top + bottom) * 0.5f, _sitter.transform.position.z);
                float distance = (top - bottom) * 0.5f / Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                _camera.transform.position = middle + new Vector3(0f, 0.05f, distance);
                _camera.transform.LookAt(middle);
            }

            Color sunColour = default, ambient = RenderSettings.ambientLight;
            float intensity = 0f, shadows = 0f;
            Quaternion rotation = Quaternion.identity;
            SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            try
            {
                if (_sun != null)
                {
                    sunColour = _sun.color;
                    intensity = _sun.intensity;
                    shadows = _sun.shadowStrength;
                    rotation = _sun.transform.rotation;
                    _sun.color = new Color(1f, 0.96f, 0.9f);
                    _sun.intensity = 1.05f;
                    _sun.shadowStrength = 0.6f;
                    _sun.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
                    var light = new Color(0.46f, 0.47f, 0.55f);
                    RenderSettings.ambientLight = light;
                    var flat = new SphericalHarmonicsL2();
                    flat.AddAmbientLight(light);
                    RenderSettings.ambientProbe = flat;
                }
                _camera.targetTexture = target;
                _camera.Render();
            }
            finally
            {
                _camera.targetTexture = null;
                if (_sun != null)
                {
                    _sun.color = sunColour;
                    _sun.intensity = intensity;
                    _sun.shadowStrength = shadows;
                    _sun.transform.rotation = rotation;
                    RenderSettings.ambientLight = ambient;
                    RenderSettings.ambientProbe = probe;
                }
            }
        }

        public void Dispose()
        {
            if (_studio != null) UnityEngine.Object.Destroy(_studio);
        }
    }
}
