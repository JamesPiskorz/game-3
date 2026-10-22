using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Characters.Content;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.ContentAuthoring
{
    /// <summary>
    /// The print library: tiling patterns for clothing, drawn by Tools/Textures/make_textures.py (prints.py) into
    /// Content/Patterns as pattern.&lt;id&gt;_01.png, red = how much of the pattern colour shows, green = ink (the print's
    /// drawn lines and shading), and listed with their catalog entries (name, style tags, tiles per metre, weight) in
    /// Patterns/library.json. The stand-ins' catalog uses twelve of them; the generated content's uses them all.
    /// </summary>
    public static class PatternTextures
    {
        public const string Folder = "Assets/_Game/Characters/Content/Patterns";
        public const string LibraryPath = Folder + "/library.json";

        sealed class Library
        {
            public List<PatternEntry> Patterns = new List<PatternEntry>();
        }

        /// <summary>Every print in the library, as catalog entries (copies: changing them changes nothing else).</summary>
        public static List<PatternEntry> Entries()
        {
            if (!File.Exists(LibraryPath)) return new List<PatternEntry>();
            Library library = JsonConvert.DeserializeObject<Library>(File.ReadAllText(LibraryPath),
                new JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                    { NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy() } });
            return library?.Patterns ?? new List<PatternEntry>();
        }

        /// <summary>Every print's ID.</summary>
        public static IEnumerable<string> All => Entries().Select(e => e.Id);

        public static Texture2D Load(string id) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/{id}.png");

        /// <summary>Sets every print's import settings: linear data, repeating, mipmapped, compressed in high quality
        /// (BC7 on PC, which keeps the three channels apart, where the old DXT bled one into another; a third of the
        /// memory of the uncompressed tiles, 200 of them being 50 MB). The textures themselves come from
        /// Tools/Textures/make_textures.py.</summary>
        [MenuItem("Game/Characters/Content/Import Pattern Textures")]
        public static void Generate()
        {
            AssetDatabase.Refresh();
            foreach (string id in All)
            {
                string path = $"{Folder}/{id}.png";
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                {
                    Debug.LogError($"[Content] No print texture at {path}: run Tools/Textures/make_textures.py.");
                    continue;
                }
                bool changed = importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Repeat || !importer.mipmapEnabled
                    || importer.textureCompression != TextureImporterCompression.CompressedHQ || importer.anisoLevel != 4;
                if (!changed) continue;
                importer.sRGBTexture = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
        }
    }
}
