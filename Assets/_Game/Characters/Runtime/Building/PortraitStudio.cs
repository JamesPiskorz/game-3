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
    /// Renders characters into textures for UI: hidden sitters in a studio far below the scene, re-dressed for each
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
        static int _studios; // each studio gets its own spot, so one never shows in another's pictures

        readonly CharacterBuilder _builder;
        readonly Light _sun;
        readonly GameObject _studio;
        readonly Camera _camera;
        ModularCharacter _sitter, _partner;
        bool _warm;

        public PortraitStudio(CharacterBuilder builder, Color backdrop, Light sun = null)
        {
            _builder = builder;
            _sun = sun;
            // A plain scene object, so it goes with its scene (a DontSave object would outlive it and walk into the next
            // scene's pictures).
            _studio = new GameObject("Portrait studio");
            _studio.transform.position = StudioPosition + new Vector3(20f * _studios++, 0f, 0f);
            _camera = new GameObject("Portrait camera").AddComponent<Camera>();
            _camera.transform.SetParent(_studio.transform, false);
            _camera.enabled = false; // rendered by hand
            GameCamera.Configure(_camera, sceneGrading: false); // no screen vignette on every portrait
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
            Sit(ref _sitter, dna, outfit, "Sitter", Vector3.zero, 0f, expression);
            if (_partner != null) _partner.gameObject.SetActive(false);

            Transform head = Head(_sitter);
            if (framing == Framing.Face)
            {
                // From a little below eye level, the head with some room above for big hair; a tall picture stands
                // back further for the head to fit across.
                float fit = Mathf.Max(1f, 0.85f * target.height / target.width);
                Vector3 look = head.position + new Vector3(0f, 0.1f, 0f);
                _camera.transform.position = look + new Vector3(0f, -0.02f, 1.12f * fit);
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
            Shoot(target);
        }

        /// <summary>Two people side by side, turned a little towards each other, from the waist up: a photo together.</summary>
        public void RenderPair(CharacterDna left, CharacterDna right, string outfit, RenderTexture target)
        {
            // The camera looks back along -z, so +x is the left of the picture.
            Sit(ref _sitter, left, outfit, "Sitter", new Vector3(0.3f, 0f, 0f), -14f, "delighted");
            Sit(ref _partner, right, outfit, "Partner", new Vector3(-0.3f, 0f, 0f), 14f, "happy");
            float headY = Mathf.Max(Head(_sitter).position.y, Head(_partner).position.y);
            // Wide enough for both, from above their hair to about the waist.
            float aspect = (float)target.width / target.height;
            float halfWidth = 0.62f, halfHeight = Mathf.Max(0.42f, halfWidth / aspect);
            Vector3 middle = new Vector3(_studio.transform.position.x, headY + 0.28f - halfHeight, _studio.transform.position.z);
            float distance = halfHeight / Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _camera.transform.position = middle + new Vector3(0f, 0.04f, distance);
            _camera.transform.LookAt(middle);
            Shoot(target);
        }

        void Sit(ref ModularCharacter sitter, CharacterDna dna, string outfit, string name, Vector3 at, float turn, string expression)
        {
            if (sitter == null)
            {
                sitter = _builder.Build(dna, outfit, _studio.transform);
                sitter.name = name;
                if (sitter.GetComponent<IdleMotion>() is IdleMotion idle) idle.enabled = false; // a still pose
            }
            else
            {
                sitter.gameObject.SetActive(true);
                _builder.Apply(sitter, dna, outfit);
            }
            sitter.transform.localPosition = at;
            sitter.transform.localRotation = Quaternion.Euler(0f, turn, 0f);
            foreach (SpringBoneChain chain in sitter.GetComponents<SpringBoneChain>()) chain.enabled = false;
            // Several shots can be taken in one frame: skin for each render, not once per frame, or new parts show stale.
            foreach (SkinnedMeshRenderer renderer in sitter.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.forceMatrixRecalculationPerRender = true;
            sitter.GetComponent<CharacterLod>()?.Apply(0f);
            Poses.Relaxed(sitter.transform);
            if (sitter.GetComponent<FaceExpression>() is FaceExpression face)
            {
                face.Show(expression, 0.45f, float.PositiveInfinity);
                face.Snap();
            }
        }

        static Transform Head(ModularCharacter character) => character.GetComponentsInChildren<Transform>().First(t => t.name == "Head");

        void Shoot(RenderTexture target)
        {
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
                    LightingLook studio = Studio;
                    _sun.color = studio.SunColour;
                    _sun.intensity = studio.SunIntensity;
                    _sun.shadowStrength = studio.ShadowStrength;
                    _sun.transform.rotation = Quaternion.Euler(studio.SunAngles);
                    SceneLighting.ApplyAmbient(studio);
                }
                _camera.targetTexture = target;
                // The first render after startup can come out with placeholder shaders (black); throw one away.
                if (!_warm)
                {
                    _camera.Render();
                    _warm = true;
                }
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

        // The studio preset, or neutral daylight if the content has no lighting library.
        LightingLook Studio
        {
            get
            {
                LightingPreset preset = _builder.Content != null && _builder.Content.Lighting != null ? _builder.Content.Lighting.Get(LightingLibrary.Studio) : null;
                if (preset != null) return preset.Look;
                Color ambient = new Color(0.46f, 0.47f, 0.55f);
                return new LightingLook
                {
                    SunColour = new Color(1f, 0.96f, 0.9f), SunIntensity = 1.05f, SunAngles = new Vector3(35f, 160f, 0f), ShadowStrength = 0.6f,
                    AmbientSky = ambient, AmbientEquator = ambient, AmbientGround = ambient,
                };
            }
        }

        public void Dispose()
        {
            if (_studio != null) UnityEngine.Object.Destroy(_studio);
        }
    }
}
