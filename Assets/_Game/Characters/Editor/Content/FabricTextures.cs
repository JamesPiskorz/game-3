using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.ContentAuthoring
{
    /// <summary>
    /// The fabric library: what each generated garment is made of (denim, a rib knit, leather, satin, lace...), drawn by
    /// Tools/Textures/make_textures.py (fabrics.py) into Content/Fabrics as fabric.&lt;id&gt;.png, a seamless grey tile of
    /// the cloth's surface, and listed in Fabrics/library.json with its mean brightness and the metres of cloth a tile
    /// covers. Each fabric gets a cloth material (Fabrics/Materials/Fabric_&lt;id&gt;.mat): the cloth template with the tile
    /// as its base map, repeating per metre of the garment's fabric UVs and brightened by its mean, so the cloth keeps
    /// its colour on average, and lifted by its library lift (its structure added as light, so it shows on dark cloth).
    /// A generated part's sidecar names its fabric; CharacterModelImporter gives it that fabric's material.
    /// Headless: unity run . -- -executeMethod Game.Characters.ContentAuthoring.FabricTextures.Generate
    /// </summary>
    public static class FabricTextures
    {
        public const string Folder = "Assets/_Game/Characters/Content/Fabrics";
        public const string MaterialFolder = Folder + "/Materials";
        public const string LibraryPath = Folder + "/library.json";
        const string ClothTemplate = "Assets/_Game/Characters/Content/StandIn/Materials/StandIn_Cloth.mat";

        // A fabric's tile is darker than the cloth's colour by its mean; the gain undoes that, up to this much (lace's
        // open net is meant to read darker than the cloth).
        const float MaxGain = 1.35f;

        public sealed class Fabric
        {
            public string Id;
            public string Name;
            public float Mean = 1f;
            public float TileMetres = 0.25f;
            public List<string> Tags = new List<string>();
            public bool Printable = true;
            public List<string> PrintTags = new List<string>();
            public float Weight = 1f;
            public float Lift;
        }

        sealed class Library
        {
            public List<Fabric> Fabrics = new List<Fabric>();
        }

        public static List<Fabric> Entries()
        {
            if (!File.Exists(LibraryPath)) return new List<Fabric>();
            return JsonConvert.DeserializeObject<Library>(File.ReadAllText(LibraryPath),
                new JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                    { NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy() } })?.Fabrics ?? new List<Fabric>();
        }

        public static string MaterialPath(string id) => $"{MaterialFolder}/Fabric_{id}.mat";

        public static Material Material(string id) => string.IsNullOrEmpty(id) ? null : AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(id));

        [MenuItem("Game/Characters/Content/Write Fabric Materials")]
        public static void Generate()
        {
            AssetDatabase.Refresh();
            var template = AssetDatabase.LoadAssetAtPath<Material>(ClothTemplate);
            if (template == null)
            {
                Debug.LogError($"[Content] Fabric materials need the cloth template {ClothTemplate}.");
                return;
            }
            Directory.CreateDirectory(MaterialFolder);
            int made = 0;
            foreach (Fabric fabric in Entries())
            {
                string texturePath = $"{Folder}/fabric.{fabric.Id}.png";
                if (!(AssetImporter.GetAtPath(texturePath) is TextureImporter importer))
                {
                    Debug.LogError($"[Content] No fabric texture at {texturePath}: run Tools/Textures/make_textures.py.");
                    continue;
                }
                // Linear (the tile is shading, multiplied in), repeating, mipmapped so it fades to its mean at a
                // distance, and anisotropic so it holds up on cloth seen edge on.
                if (importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Repeat || importer.anisoLevel != 8 || importer.maxTextureSize != 512)
                {
                    importer.sRGBTexture = false;
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.mipmapEnabled = true;
                    importer.anisoLevel = 8;
                    importer.maxTextureSize = 512;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.SaveAndReimport();
                }
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

                string path = MaterialPath(fabric.Id);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool created = material == null;
                if (created) material = new Material(template);
                else material.CopyPropertiesFromMaterial(template);
                material.name = $"Fabric_{fabric.Id}";
                material.SetTexture("_BaseMap", texture);
                float perMetre = 1f / Mathf.Max(fabric.TileMetres, 0.01f);
                material.SetTextureScale("_BaseMap", new Vector2(perMetre, perMetre));
                material.SetFloat("_BaseGain", Mathf.Min(1f / Mathf.Max(fabric.Mean, 0.05f), MaxGain));
                material.SetFloat("_BaseLift", fabric.Lift);
                if (created) AssetDatabase.CreateAsset(material, path);
                else EditorUtility.SetDirty(material);
                made++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Content] Wrote {made} fabric materials in {MaterialFolder}.");
        }

        /// <summary>Fabric IDs in the library.</summary>
        public static IEnumerable<string> Ids => Entries().Select(f => f.Id);

        /// <summary>
        /// The whole texture library into the game, in order: fabric materials, print import settings, the generated
        /// parts reimported so each wears its fabric's material, and the generated content written.
        /// Headless: unity run . -- -executeMethod Game.Characters.ContentAuthoring.FabricTextures.BuildAll
        /// </summary>
        [MenuItem("Game/Characters/Content/Build Texture Library")]
        public static void BuildAll()
        {
            Generate();
            PatternTextures.Generate();
            foreach (string fbx in Directory.GetFiles(GeneratedContent.Folder, "cloth.*.fbx"))
                AssetDatabase.ImportAsset(fbx.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
            GeneratedContent.Write();
        }
    }
}
