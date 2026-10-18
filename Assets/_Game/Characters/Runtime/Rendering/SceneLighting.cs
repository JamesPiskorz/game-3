using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Characters.Rendering
{
    /// <summary>
    /// Applies a <see cref="LightingLook"/> to the open scene: sun, gradient ambient, sky, fog, and a grading volume
    /// (graphics plan G0.4-G0.5). The town, creator, menu and the lighting sheet all light through here. The grading
    /// volume sits on the Default layer, which main cameras see and the portrait studio and review cameras don't
    /// (<see cref="GameCamera.Configure"/>), so a vignette frames the screen, not every portrait.
    /// </summary>
    public sealed class SceneLighting
    {
        public const string SkyShader = "Game/Environment/Sky";
        static readonly int SkyTop = Shader.PropertyToID("_SkyTop");
        static readonly int SkyHorizon = Shader.PropertyToID("_SkyHorizon");
        static readonly int Clouds = Shader.PropertyToID("_Clouds");
        static readonly int CloudColour = Shader.PropertyToID("_CloudColour");

        readonly Light _sun;
        readonly Camera _camera;
        Material _sky;
        VolumeProfile _profile;
        Vignette _vignette;
        ColorAdjustments _adjustments;
        SplitToning _split;

        public SceneLighting(Light sun, Camera camera)
        {
            _sun = sun;
            _camera = camera;
        }

        /// <param name="backdrop">A solid background instead of the sky (interiors, the creator), or null for the sky.</param>
        /// <param name="vignette">False for sheets of many tiles, where each tile would get dark corners.</param>
        public void Apply(in LightingLook look, Color? backdrop = null, bool fog = true, bool vignette = true)
        {
            if (_sun != null)
            {
                _sun.color = look.SunColour;
                _sun.intensity = look.SunIntensity;
                _sun.transform.rotation = Quaternion.Euler(look.SunAngles);
                _sun.shadowStrength = look.ShadowStrength;
            }
            ApplyAmbient(look);

            if (_camera != null)
            {
                if (backdrop.HasValue)
                {
                    _camera.clearFlags = CameraClearFlags.SolidColor;
                    _camera.backgroundColor = backdrop.Value;
                }
                else
                {
                    if (_sky == null) _sky = new Material(Shader.Find(SkyShader)) { name = "Sky", hideFlags = HideFlags.DontSave };
                    _sky.SetColor(SkyTop, look.SkyTop);
                    _sky.SetColor(SkyHorizon, look.SkyHorizon);
                    _sky.SetFloat(Clouds, look.Clouds);
                    // Clouds are lit pale by day and barely lighter than the sky at night.
                    _sky.SetColor(CloudColour, Color.Lerp(look.SkyHorizon, Color.white, Mathf.Lerp(0.55f, 0.06f, look.Night)));
                    RenderSettings.skybox = _sky;
                    _camera.clearFlags = CameraClearFlags.Skybox;
                    _camera.backgroundColor = look.SkyHorizon;
                }
            }

            RenderSettings.fog = fog && look.FogEnd > look.FogStart && look.FogEnd > 0f;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = look.FogColour;
            RenderSettings.fogStartDistance = look.FogStart;
            RenderSettings.fogEndDistance = look.FogEnd;

            EnsureGrading();
            _vignette.intensity.Override(vignette ? look.Vignette : 0f);
            _adjustments.saturation.Override(look.Saturation);
            _adjustments.contrast.Override(look.Contrast);
            _adjustments.colorFilter.Override(look.ColourFilter.a > 0f ? look.ColourFilter : Color.white);
            _split.shadows.Override(Neutral(look.SplitShadows));
            _split.highlights.Override(Neutral(look.SplitHighlights));
        }

        /// <summary>
        /// Ambient from the sky above, the horizon and the ground below. The shaders read the ambient probe, so it's built
        /// here: the horizon colour everywhere, plus wide lobes up and down for the sky and ground (exactly flat when the
        /// three match).
        /// </summary>
        public static void ApplyAmbient(in LightingLook look)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = look.AmbientEquator;
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(look.AmbientEquator);
            probe.AddDirectionalLight(Vector3.up, look.AmbientSky - look.AmbientEquator, 1f);
            probe.AddDirectionalLight(Vector3.down, look.AmbientGround - look.AmbientEquator, 1f);
            RenderSettings.ambientProbe = probe;
        }

        // Split toning's neutral is mid grey; an unset (black, transparent) colour means neutral too.
        static Color Neutral(Color colour) => colour.a > 0f ? colour : new Color(0.5f, 0.5f, 0.5f, 1f);

        void EnsureGrading()
        {
            if (_profile != null) return;
            // A plain scene object, so it goes with its scene (a DontSave one would outlive it). Default layer: main cameras
            // see it, the portrait studio and review cameras don't.
            var go = new GameObject("Grading") { layer = 0 };
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.hideFlags = HideFlags.DontSave;
            _vignette = _profile.Add<Vignette>();
            _vignette.smoothness.Override(0.4f);
            _adjustments = _profile.Add<ColorAdjustments>();
            _split = _profile.Add<SplitToning>();
            volume.sharedProfile = _profile;
        }
    }
}
