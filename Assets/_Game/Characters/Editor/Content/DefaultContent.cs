using System.IO;
using Game.Characters.Rendering;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.ContentAuthoring
{
    /// <summary>Creates the default content assets that the review tools and the stand-in library rely on.</summary>
    public static class DefaultContent
    {
        public const string PaletteFolder = "Assets/_Game/Characters/Content/Palettes";
        public const string SkinPalettePath = PaletteFolder + "/SkinTones.asset";

        public static SkinTonePalette SkinPalette()
        {
            var palette = AssetDatabase.LoadAssetAtPath<SkinTonePalette>(SkinPalettePath);
            return palette != null ? palette : ResetSkinPalette();
        }

        /// <summary>
        /// Writes the starting skin ramp: eight hand-picked stops, light to dark. Shades get warmer and more
        /// saturated as tones darken rather than just darker, and each stop should be retuned by eye in the
        /// skin ramp sheet under every lighting setup.
        /// </summary>
        [MenuItem("Game/Characters/Content/Reset Default Skin Palette")]
        public static SkinTonePalette ResetSkinPalette()
        {
            var palette = ScriptableObject.CreateInstance<SkinTonePalette>();
            palette.Stops = new[]
            {
                Stop("#FCE7DA", "#EFB8AA", "#FFF4EE"),
                Stop("#F6D2BC", "#E2A08D", "#FFEADF"),
                Stop("#EAB995", "#CB876A", "#F9DCC4"),
                Stop("#D49F78", "#AE6C50", "#F0C6A6"),
                Stop("#B47D58", "#8C4F36", "#E0AC88"),
                Stop("#8F5D3E", "#6B3826", "#C99472"),
                Stop("#6B422B", "#4D261A", "#AE7856"),
                Stop("#4B2D1E", "#341813", "#946247"),
            };

            Directory.CreateDirectory(PaletteFolder);
            var existing = AssetDatabase.LoadAssetAtPath<SkinTonePalette>(SkinPalettePath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(palette, SkinPalettePath);
                return palette;
            }
            EditorUtility.CopySerialized(palette, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(palette);
            AssetDatabase.SaveAssets();
            return existing;
        }

        static SkinTonePalette.Stop Stop(string baseHex, string shadeHex, string rimHex) => new SkinTonePalette.Stop
        {
            Base = Hex(baseHex),
            Shade = Hex(shadeHex),
            Rim = Hex(rimHex),
        };

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color colour) ? colour : Color.magenta;
    }
}
