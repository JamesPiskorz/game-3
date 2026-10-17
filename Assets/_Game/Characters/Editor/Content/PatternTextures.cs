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
            ("pattern.hearts_01", (u, v) => Heart((u - 0.5f) * 3.2f, (v - 0.45f) * 3.2f)),
            ("pattern.stars_01", (u, v) => Polar(u, v, (r, a) => InStar(r, a + Mathf.PI / 5f, 0.24f, 0.1f))),
            ("pattern.pinstripe_01", (u, v) => Band(u, 0.5f, 0.05f)),
            ("pattern.floral_01", (u, v) => Mathf.Max(
                Polar(u, v, (r, a) => r < 0.27f * (0.45f + 0.55f * Mathf.Abs(Mathf.Cos(2.5f * a)))),
                Polar(u, v, (r, a) => r < 0.07f) * 0.5f)),
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

        /// <summary>The classic heart curve, (x² + y² - 1)³ - x²y³ &lt; 0, upright.</summary>
        static float Heart(float x, float y)
        {
            float q = x * x + y * y - 1f;
            return q * q * q - x * x * y * y * y < 0f ? 1f : 0f;
        }

        /// <summary>
        /// Inside a five-pointed star with straight edges: fold the angle into the half-sector between a point and
        /// the next notch, then test which side of that edge the point is on.
        /// </summary>
        static bool InStar(float r, float a, float outer, float inner)
        {
            float sector = 2f * Mathf.PI / 5f;
            float phi = Mathf.Repeat(a, sector);
            if (phi > sector / 2f) phi = sector - phi;
            var p = new Vector2(r * Mathf.Cos(phi), r * Mathf.Sin(phi));
            var tip = new Vector2(outer, 0f);
            var notch = new Vector2(inner * Mathf.Cos(sector / 2f), inner * Mathf.Sin(sector / 2f));
            Vector2 edge = notch - tip;
            float side = edge.x * (p.y - tip.y) - edge.y * (p.x - tip.x);
            float origin = edge.x * (0f - tip.y) - edge.y * (0f - tip.x);
            return side * origin >= 0f;
        }

        /// <summary>A shape described in polar terms around the tile's centre.</summary>
        static float Polar(float u, float v, Func<float, float, bool> inside)
        {
            float du = u - 0.5f, dv = v - 0.5f;
            return inside(Mathf.Sqrt(du * du + dv * dv), Mathf.Atan2(dv, du) + Mathf.PI / 2f) ? 1f : 0f;
        }

        static float Band(float t, float centre, float halfWidth) => Mathf.Abs(t - centre) < halfWidth ? 1f : 0f;

        static float Dot(float u, float v, float radius)
        {
            float du = u - 0.5f, dv = v - 0.5f;
            return du * du + dv * dv < radius * radius ? 1f : 0f;
        }
    }
}
