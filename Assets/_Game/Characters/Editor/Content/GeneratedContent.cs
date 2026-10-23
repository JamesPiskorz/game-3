using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.Import;
using Game.Characters.Modular;
using Game.Characters.Springs;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.ContentAuthoring
{
    /// <summary>
    /// Writes Content/GeneratedContent.asset and Content/GeneratedCatalog.json: the content for running the game on the
    /// generated body and its generated parts (decision 0008) instead of the stand-ins. The catalog is the stand-in
    /// catalog's sliders, styles, palettes, archetypes and contexts with the parts replaced by the generated ones, made
    /// from their sidecars (Tools/Blender/make_generated_outfit.py and make_generated_hair.py write each part's tags,
    /// lean, contexts and weight from outfits.json and hairs.json). What the generated parts can't serve is left out,
    /// so the catalog validates and generation never fails: an outfit context the parts can't dress (work clothes need
    /// an elegant or bookish top, a rainy day a coat) and face marks that have no parts. It adds swimming, sleepwear
    /// and formal occasions (<see cref="ExtraContexts"/>), and uniforms to work clothes. The town copes with a missing
    /// context (<c>SimWorld.OutfitAt</c> falls back to everyday clothes). Which content the game uses is
    /// <see cref="GameContent"/>'s switch.
    /// Headless: unity run . -- -executeMethod Game.Characters.ContentAuthoring.GeneratedContent.Write
    /// </summary>
    public static class GeneratedContent
    {
        public const string Path = "Assets/_Game/Characters/Content/GeneratedContent.asset";
        public const string CatalogPath = "Assets/_Game/Characters/Content/GeneratedCatalog.json";
        public const string Folder = CharacterModelImporter.Folder + "Generated/";
        public const string BodyPath = Folder + "GeneratedBody.fbx";

        [Serializable]
        sealed class Sidecar
        {
            public string kind = "part";
            public string name;
            public string slot;
            public int regions = 1;
            public string[] tags = Array.Empty<string>();
            public float lean;
            public string[] contexts = Array.Empty<string>();
            public float weight = 1f;
            public string[] blocksSlots = Array.Empty<string>();
            public string[] excludesTags = Array.Empty<string>();
            public string[] requiresTags = Array.Empty<string>();
            public bool patternable;
            public string fabric;
            public string[] fabrics = Array.Empty<string>();
        }

        public static CharacterContent Load() => AssetDatabase.LoadAssetAtPath<CharacterContent>(Path);

        /// <summary>
        /// The height range of a generated part's cloth: its fabric UVs' v (height in metres, garment_tools.fabric_uvs)
        /// over the body's panels and the legs, not the sleeves (whose v runs out along the arm) or the feet (u: the
        /// front panel and the legs lined up with it round 0, the back round 10, the arms 20-40, a leg that couldn't be
        /// lined up 40-60, the feet beyond).
        /// </summary>
        static (float Bottom, float Top) ClothHeights(CharacterPart prefab)
        {
            float bottom = float.MaxValue, top = float.MinValue;
            foreach (SkinnedMeshRenderer renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                using (Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(renderer.sharedMesh))
                {
                    if (!data[0].HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)) continue;
                    var list = new NativeArray<Vector2>(data[0].vertexCount, Allocator.Temp);
                    data[0].GetUVs(0, list);
                    foreach (Vector2 uv in list)
                        if (uv.x < 15f || (uv.x >= 40f && uv.x < 60f))
                        {
                            bottom = Mathf.Min(bottom, uv.y);
                            top = Mathf.Max(top, uv.y);
                        }
                    list.Dispose();
                }
            }
            return bottom < top ? (bottom, top) : (0f, 0f);
        }

        [MenuItem("Game/Characters/Content/Write Generated Content")]
        public static void WriteMenu() => Write();

        public static CharacterContent Write()
        {
            CharacterContent standIn = StandInContent.Load();
            var body = AssetDatabase.LoadAssetAtPath<ModularCharacter>(BodyPath);
            if (standIn == null || body == null)
            {
                Debug.LogError($"[Content] Generated content needs the stand-in content ({StandInContent.Path}) and the generated body ({BodyPath}).");
                return null;
            }

            var parts = new List<(PartEntry Entry, CharacterPart Prefab)>();
            foreach (string sidecarPath in Directory.GetFiles(Folder, "*.character.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                Sidecar sidecar = JsonUtility.FromJson<Sidecar>(File.ReadAllText(sidecarPath));
                if (sidecar.kind != "part") continue;
                string id = System.IO.Path.GetFileName(sidecarPath).Replace(".character.json", "");
                var prefab = AssetDatabase.LoadAssetAtPath<CharacterPart>(Folder + id + ".fbx");
                if (prefab == null)
                {
                    Debug.LogError($"[Content] {id} has a sidecar but no imported part; leaving it out.");
                    continue;
                }
                parts.Add((new PartEntry
                {
                    Id = id, Name = string.IsNullOrEmpty(sidecar.name) ? ContentCatalog.Humanize(id) : sidecar.name,
                    Slot = sidecar.slot, Regions = Mathf.Max(1, sidecar.regions), Tags = sidecar.tags.ToList(),
                    Lean = sidecar.lean, Contexts = sidecar.contexts.ToList(), Weight = sidecar.weight > 0f ? sidecar.weight : 1f,
                    BlocksSlots = sidecar.blocksSlots.ToList(), ExcludesTags = sidecar.excludesTags.ToList(), RequiresTags = sidecar.requiresTags.ToList(),
                    Patternable = sidecar.patternable,
                    // Its own fabric first, then the others it can be made in.
                    Fabrics = string.IsNullOrEmpty(sidecar.fabric) ? new List<string>()
                        : new[] { sidecar.fabric }.Concat(sidecar.fabrics.Where(f => f != sidecar.fabric)).Distinct().ToList(),
                }, prefab));
                (parts[parts.Count - 1].Entry.ClothBottom, parts[parts.Count - 1].Entry.ClothTop) = ClothHeights(prefab);
            }

            ContentCatalog catalog = Catalog(standIn.LoadCatalog(), parts.Select(p => p.Entry), out List<string> notes);
            List<string> errors = catalog.Validate();
            if (errors.Count > 0)
            {
                Debug.LogError("[Content] The generated catalog doesn't validate:\n" + string.Join("\n", errors));
                return null;
            }
            File.WriteAllText(CatalogPath, CatalogSerializer.ToJson(catalog));
            AssetDatabase.ImportAsset(CatalogPath);

            CharacterContent content = Load();
            bool created = content == null;
            if (created) content = ScriptableObject.CreateInstance<CharacterContent>();
            content.Catalog = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogPath);
            content.BodyPrefab = body;
            content.SkinPalette = standIn.SkinPalette;
            content.Lighting = standIn.Lighting;
            content.Patterns = PatternTextures.All.Select(id => new PatternBinding { Id = id, Texture = PatternTextures.Load(id) }).ToList();
            content.Fabrics = FabricTextures.Ids.Select(id => new FabricBinding { Id = id, Material = FabricTextures.Material(id) }).ToList();
            content.Sliders = standIn.Sliders.ToList();
            content.Styles = standIn.Styles.ToList();
            content.SpringColliders = InBoneAxes(standIn.SpringColliders, body.transform);
            content.Parts = parts.Select(p => new PartBinding { Id = p.Entry.Id, Prefab = p.Prefab }).ToList();
            content.Reload();
            if (created) AssetDatabase.CreateAsset(content, Path);
            EditorUtility.SetDirty(content);
            AssetDatabase.SaveAssets();

            errors = content.Validate();
            if (errors.Count > 0) Debug.LogError("[Content] Generated content is incomplete:\n" + string.Join("\n", errors));
            else Debug.Log($"[Content] Wrote {Path}: {parts.Count} generated parts" +
                           (notes.Count > 0 ? "; left out: " + string.Join("; ", notes) : ""));
            return content;
        }

        /// <summary>
        /// <paramref name="standIn"/>'s catalog with its parts replaced by <paramref name="parts"/>, less what those parts
        /// can't serve: contexts no outfit can be made for (each left-out thing is described in <paramref name="notes"/>),
        /// and archetypes' chances of face marks there are no parts for. A copy: <paramref name="standIn"/> is untouched.
        /// </summary>
        public static ContentCatalog Catalog(ContentCatalog standIn, IEnumerable<PartEntry> parts, out List<string> notes)
        {
            notes = new List<string>();
            ContentCatalog catalog = CatalogSerializer.FromJson(CatalogSerializer.ToJson(standIn));
            catalog.Parts.Clear();
            catalog.Parts.AddRange(parts);
            // The whole print library (Tools/Textures/prints.py), where the stand-ins use a dozen, and the fabric
            // library (Tools/Textures/fabrics.py) the parts are made in.
            List<PatternEntry> prints = PatternTextures.Entries();
            if (prints.Count > 0)
            {
                catalog.Patterns.Clear();
                catalog.Patterns.AddRange(prints);
            }
            catalog.Fabrics.Clear();
            catalog.Fabrics.AddRange(FabricTextures.Entries().Select(f => new FabricEntry
                {
                    Id = f.Id, Name = f.Name, Tags = f.Tags.ToList(), Printable = f.Printable, PrintTags = f.PrintTags.ToList(), Weight = f.Weight,
                    Colours = f.Colours.Select(hex => new PaletteColour { Hex = hex }).ToList(), ColourChance = f.ColourChance,
                }));
            // Fabric choices the library lacks are dropped, so the catalog validates.
            foreach (PartEntry part in catalog.Parts)
                part.Fabrics.RemoveAll(f => catalog.Fabric(f) == null);
            foreach (OutfitContext extra in ExtraContexts())
                if (catalog.Context(extra.Id) == null) catalog.Contexts.Add(extra);
            AddUniforms(catalog);
            catalog = CatalogSerializer.FromJson(CatalogSerializer.ToJson(catalog)); // re-indexes the parts

            bool Fits(PartEntry part, OutfitContext context) =>
                (part.Contexts.Count == 0 || part.Contexts.Contains(context.Id))
                && (context.OnlyTags.Count == 0 || part.Tags.Any(context.OnlyTags.Contains));

            foreach (OutfitContext context in catalog.Contexts.ToList())
            {
                string[] needed = context.Shoes ? new[] { Slots.Top, Slots.Bottom, Slots.Shoes } : new[] { Slots.Top, Slots.Bottom };
                string[] missing = needed.Where(slot => !catalog.PartsInSlot(slot).Any(p => Fits(p, context))).ToArray();
                bool coatless = context.Outer && !catalog.PartsInSlot(Slots.Outer).Any(p => Fits(p, context));
                if (missing.Length == 0 && !coatless) continue;
                catalog.Contexts.Remove(context);
                notes.Add($"the '{context.Id}' outfit (no " + string.Join(", ", missing.Concat(coatless ? new[] { Slots.Outer } : Array.Empty<string>())) +
                          (context.OnlyTags.Count > 0 ? $" tagged {string.Join("/", context.OnlyTags)}" : "") + ")");
            }
            foreach (PartEntry part in catalog.Parts)
                part.Contexts.RemoveAll(c => catalog.Context(c) == null);

            var marks = new SortedSet<string>(StringComparer.Ordinal);
            foreach (StyleArchetype archetype in catalog.Archetypes)
                foreach (string mark in archetype.FaceChances.Keys.Where(m => m != Makeup.Lipstick && !catalog.PartsInSlot(m).Any()).ToList())
                {
                    archetype.FaceChances.Remove(mark);
                    marks.Add(mark);
                }
            if (marks.Count > 0) notes.Add("face marks with no parts (" + string.Join(", ", marks) + ")");
            return CatalogSerializer.FromJson(CatalogSerializer.ToJson(catalog));
        }

        /// <summary>
        /// Occasions only the generated parts dress: swimming and sleepwear, barefoot, from parts tagged "swim" and
        /// "sleep" only, so swimwear and lingerie never turn up in everyday outfits; and formal wear (gowns, a wedding
        /// dress, a dress shirt and trousers), from parts tagged "formal", made in formal fabrics wherever a part can be
        /// (a velvet blazer, patent shoes) and printed only with formal prints. The town dresses for them where the catalog
        /// has them (SimWorld.OutfitAt): nightwear at home at night, swimwear at the beach, formal wear at the town's
        /// occasional events (TownEvents). The creator lists them.
        /// </summary>
        static IEnumerable<OutfitContext> ExtraContexts()
        {
            yield return new OutfitContext { Id = "swim", Name = "Swimming", TagWeights = { ["swim"] = 1f }, OnlyTags = { "swim" }, Shoes = false };
            yield return new OutfitContext { Id = "sleep", Name = "Sleepwear", TagWeights = { ["sleep"] = 1f }, OnlyTags = { "sleep" }, Shoes = false };
            yield return new OutfitContext
            {
                Id = "formal", Name = "Formal", TagWeights = { ["formal"] = 1f }, OnlyTags = { "formal" }, Shoes = true,
                FabricTags = { "formal" }, StrictFabrics = true, PrintTags = { "formal" },
            };
        }

        /// <summary>
        /// Work clothes the stand-ins have no tag for: uniforms (scrubs) go to work alongside elegant and bookish clothes.
        /// </summary>
        static void AddUniforms(ContentCatalog catalog)
        {
            OutfitContext work = catalog.Context("work");
            if (work == null || work.OnlyTags.Contains("uniform")) return;
            work.OnlyTags.Add("uniform");
            work.TagWeights["uniform"] = 1f;
        }

        /// <summary>
        /// The stand-in's spring colliders, written in the character's axes (its bones rest square with it), re-expressed
        /// in <paramref name="body"/>'s bone axes: a collider's offset and radius are in its bone's space, and the
        /// generated rig's bones keep Blender's roll, so the stand-in's offsets put the skull sphere in front of the face.
        /// </summary>
        static List<SpringColliderBinding> InBoneAxes(IEnumerable<SpringColliderBinding> bindings, Transform body)
        {
            var bones = body.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var result = new List<SpringColliderBinding>();
            foreach (SpringColliderBinding binding in bindings)
            {
                if (!bones.TryGetValue(binding.Bone, out Transform bone))
                {
                    Debug.LogWarning($"[Content] The generated body has no bone '{binding.Bone}' for a spring collider; leaving it out.");
                    continue;
                }
                Vector3 offset = bone.InverseTransformVector(body.TransformVector(binding.Offset));
                float scale = bone.lossyScale.x / Mathf.Max(body.lossyScale.x, 1e-6f);
                result.Add(new SpringColliderBinding { Bone = binding.Bone, Offset = offset, Radius = binding.Radius / Mathf.Max(scale, 1e-6f) });
            }
            return result;
        }
    }
}
