using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.ContentAuthoring
{
    /// <summary>
    /// Procedural tiling pattern masks (white = pattern colour) for clothing. Written as linear, repeating PNGs; the
    /// Toon shader reads the red channel and tiles it per metre.
    /// </summary>
    public static class PatternTextures
    {
        public const string Folder = "Assets/_Game/Characters/Content/Patterns";
        const int Size = 128;

        public static readonly (string Id, Func<float, float, float> Shape)[] All =
        {
            ("pattern.stripes_01", (u, v) => Band(v, 0.5f, 0.32f)),
            ("pattern.dots_01", (u, v) => Dot(u, v, 0.22f)),
            ("pattern.plaid_01", (u, v) => Mathf.Max(Band(u, 0.25f, 0.18f), Band(v, 0.25f, 0.18f)) * 0.75f + Mathf.Max(Band(u, 0.75f, 0.05f), Band(v, 0.75f, 0.05f)) * 0.25f),
            ("pattern.check_01", (u, v) => (u < 0.5f) ^ (v < 0.5f) ? 1f : 0f),
        };

        public static Texture2D Load(string id) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/{id}.png");

        [MenuItem("Game/Characters/Content/Generate Pattern Textures")]
        public static void Generate()
        {
            Directory.CreateDirectory(Folder);
            foreach ((string id, Func<float, float, float> shape) in All)
            {
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, linear: true);
                var pixels = new Color32[Size * Size];
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        // 4×4 supersampling keeps edges soft at any tiling scale.
                        float sum = 0f;
                        for (int sy = 0; sy < 4; sy++)
                            for (int sx = 0; sx < 4; sx++)
                                sum += shape((x + (sx + 0.5f) / 4f) / Size, (y + (sy + 0.5f) / 4f) / Size);
                        byte value = (byte)Mathf.RoundToInt(sum / 16f * 255f);
                        pixels[y * Size + x] = new Color32(value, value, value, 255);
                    }
                texture.SetPixels32(pixels);
                string path = $"{Folder}/{id}.png";
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);

                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        static float Band(float t, float centre, float halfWidth) => Mathf.Abs(t - centre) < halfWidth ? 1f : 0f;

        static float Dot(float u, float v, float radius)
        {
            float du = u - 0.5f, dv = v - 0.5f;
            return du * du + dv * dv < radius * radius ? 1f : 0f;
        }
    }
}
