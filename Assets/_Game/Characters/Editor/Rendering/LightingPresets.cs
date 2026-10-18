using System.Collections.Generic;
using Game.Characters.Building;
using Game.Characters.ContentAuthoring;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.Rendering
{
    /// <summary>
    /// Writes the lighting presets (graphics plan G0.4) as assets art can tune: day, sunset, night, interior and the
    /// portrait studio, in a library the character content points to. "Create Missing" leaves tuned presets alone;
    /// "Reset" rewrites every preset from the defaults here. Headless: -executeMethod
    /// Game.Characters.Rendering.LightingPresets.ResetToDefaults
    /// </summary>
    public static class LightingPresets
    {
        public const string Folder = "Assets/_Game/Characters/Content/Lighting";
        public const string LibraryPath = Folder + "/Lighting.asset";

        public static LightingLibrary Library() => AssetDatabase.LoadAssetAtPath<LightingLibrary>(LibraryPath);

        [MenuItem("Game/Rendering/Create Missing Lighting Presets")]
        public static void CreateMissing() => Write(false);

        [MenuItem("Game/Rendering/Reset Lighting Presets to Defaults")]
        public static void ResetToDefaults() => Write(true);

        static void Write(bool overwrite)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Game/Characters/Content", "Lighting");
            LightingLibrary library = Library();
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<LightingLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.Presets = new List<LightingPreset>
            {
                Preset(LightingLibrary.Day, DayLook(), overwrite),
                Preset(LightingLibrary.Sunset, SunsetLook(), overwrite),
                Preset(LightingLibrary.Night, NightLook(), overwrite),
                Preset(LightingLibrary.Interior, InteriorLook(), overwrite),
                Preset(LightingLibrary.Studio, StudioLook(), overwrite),
            };
            EditorUtility.SetDirty(library);

            CharacterContent content = StandInContent.Load();
            if (content != null && content.Lighting != library)
            {
                content.Lighting = library;
                EditorUtility.SetDirty(content);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[LightingPresets] {(overwrite ? "Reset" : "Checked")} {library.Presets.Count} presets in {Folder}.");
        }

        static LightingPreset Preset(string id, LightingLook look, bool overwrite)
        {
            string path = $"{Folder}/{id}.asset";
            var preset = AssetDatabase.LoadAssetAtPath<LightingPreset>(path);
            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<LightingPreset>();
                preset.Look = look;
                AssetDatabase.CreateAsset(preset, path);
            }
            else if (overwrite)
            {
                preset.Look = look;
                EditorUtility.SetDirty(preset);
            }
            return preset;
        }

        static readonly Color Lamp = new Color(1f, 0.78f, 0.52f);
        static readonly Color NeutralRim = new Color(0.25f, 0.22f, 0.2f);

        static LightingLook Common(LightingLook look)
        {
            look.ShadowStrength = 1f;
            look.LampColour = Lamp;
            look.LampIntensity = 1.2f;
            look.RimColour = NeutralRim;
            // Grading left unset is neutral: white filter, mid-grey split toning.
            if (look.ColourFilter.a <= 0f) look.ColourFilter = Color.white;
            if (look.SplitShadows.a <= 0f) look.SplitShadows = new Color(0.5f, 0.5f, 0.5f);
            if (look.SplitHighlights.a <= 0f) look.SplitHighlights = new Color(0.5f, 0.5f, 0.5f);
            return look;
        }

        static LightingLook Ambient(LightingLook look, Color sky, Color horizon, Color ground)
        {
            look.AmbientSky = sky;
            look.AmbientEquator = horizon;
            look.AmbientGround = ground;
            return look;
        }

        // Starting points for art to tune. The sun and the horizon ambient keep their earlier values, so characters read
        // as before; what's new is the sky, the ambient gradient (cooler from above, warmer and darker from the ground),
        // fog, glow and grading.
        static LightingLook DayLook()
        {
            LightingLook look = Ambient(Common(new LightingLook
            {
                SunColour = new Color(1f, 0.96f, 0.9f), SunIntensity = 1.1f, SunAngles = new Vector3(42f, 155f, 0f),
                SkyTop = new Color(0.36f, 0.58f, 0.88f), SkyHorizon = new Color(0.74f, 0.84f, 0.93f), Clouds = 0.18f,
                FogColour = new Color(0.74f, 0.84f, 0.93f), FogStart = 25f, FogEnd = 95f,
                Vignette = 0.15f,
            }), new Color(0.47f, 0.53f, 0.64f), new Color(0.42f, 0.46f, 0.55f), new Color(0.38f, 0.36f, 0.38f));
            return look;
        }

        static LightingLook SunsetLook()
        {
            LightingLook look = Ambient(Common(new LightingLook
            {
                SunColour = new Color(1f, 0.62f, 0.38f), SunIntensity = 1.05f, SunAngles = new Vector3(14f, 118f, 0f),
                SkyTop = new Color(0.3f, 0.26f, 0.52f), SkyHorizon = new Color(0.98f, 0.6f, 0.46f), Clouds = 0.28f,
                FogColour = new Color(0.86f, 0.56f, 0.5f), FogStart = 20f, FogEnd = 85f,
                Glow = 0.5f, Night = 0.25f, Vignette = 0.2f, Saturation = 5f,
                SplitShadows = new Color(0.46f, 0.43f, 0.6f), SplitHighlights = new Color(0.62f, 0.5f, 0.42f),
            }), new Color(0.34f, 0.28f, 0.44f), new Color(0.3f, 0.24f, 0.36f), new Color(0.24f, 0.18f, 0.24f));
            return look;
        }

        static LightingLook NightLook()
        {
            LightingLook look = Ambient(Common(new LightingLook
            {
                SunColour = new Color(0.45f, 0.55f, 0.8f), SunIntensity = 0.35f, SunAngles = new Vector3(48f, 150f, 0f),
                SkyTop = new Color(0.03f, 0.04f, 0.1f), SkyHorizon = new Color(0.11f, 0.13f, 0.22f), Clouds = 0.12f,
                FogColour = new Color(0.08f, 0.1f, 0.17f), FogStart = 15f, FogEnd = 75f,
                Glow = 1.5f, Night = 1f, Vignette = 0.25f, Saturation = -5f, Contrast = 5f,
                SplitShadows = new Color(0.42f, 0.45f, 0.62f), SplitHighlights = new Color(0.6f, 0.53f, 0.45f),
            }), new Color(0.12f, 0.14f, 0.24f), new Color(0.1f, 0.11f, 0.18f), new Color(0.07f, 0.07f, 0.1f));
            look.Lamps = true;
            return look;
        }

        // Half of an interior's ambient is the set's wall colour (TownStage adds it); this is the other half. Interiors
        // show their walls, not the sky, and have no fog.
        static LightingLook InteriorLook()
        {
            Color ambient = new Color(0.45f, 0.42f, 0.4f) * 0.75f;
            LightingLook look = Ambient(Common(new LightingLook
            {
                SunColour = new Color(1f, 0.93f, 0.84f), SunIntensity = 0.95f, SunAngles = new Vector3(50f, 160f, 0f),
                SkyTop = new Color(0.8f, 0.76f, 0.7f), SkyHorizon = new Color(0.8f, 0.76f, 0.7f),
                Glow = 0.3f, Vignette = 0.15f, SplitHighlights = new Color(0.56f, 0.51f, 0.45f),
            }), ambient * 1.08f, ambient, ambient * 0.85f);
            look.Lamps = true;
            look.LampIntensity = 0.7f;
            return look;
        }

        // Neutral daylight for portraits and photos.
        static LightingLook StudioLook()
        {
            Color ambient = new Color(0.46f, 0.47f, 0.55f);
            LightingLook look = Ambient(Common(new LightingLook
            {
                SunColour = new Color(1f, 0.96f, 0.9f), SunIntensity = 1.05f, SunAngles = new Vector3(35f, 160f, 0f),
                SkyTop = new Color(0.24f, 0.22f, 0.3f), SkyHorizon = new Color(0.24f, 0.22f, 0.3f),
            }), ambient, ambient, ambient);
            look.ShadowStrength = 0.6f;
            return look;
        }
    }
}
