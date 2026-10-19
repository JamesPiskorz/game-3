using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Import;
using Game.Characters.Modular;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// The generated outfits (Tools/Blender/make_generated_outfit.py, decision 0008) on the generated body as the game
    /// builds it, each in the colours its image gave it: per outfit, people from very feminine to very masculine from
    /// the front, then the two ends close up from four sides (Docs/review/generated-outfit.png).
    /// Like <see cref="GeneratedBodySheet"/>, it uses a copy of the content with the body swapped, and catalog entries
    /// made from the parts' sidecars.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.GeneratedOutfitSheet.Render
    /// </summary>
    public static class GeneratedOutfitSheet
    {
        const string Folder = CharacterModelImporter.Folder + "Generated/";
        static readonly float[] Femininity = { 1f, 0.8f, 0.6f, 0.4f, 0.2f, 0f };

        [Serializable]
        sealed class Sidecar
        {
            public string kind = "part";
            public string slot;
            public int regions = 1;
            public string[] colours = Array.Empty<string>();
            public string[] tags = Array.Empty<string>();
            public string style;
            public string outfit;
        }

        /// <summary>A generated outfit: its garments, as outfit items in the colours its image gave them.</summary>
        internal sealed class GeneratedOutfit
        {
            public string Name;
            public List<ItemDna> Items = new List<ItemDna>();
        }

        /// <summary>A generated hairstyle: its pieces (front and back), worn together.</summary>
        internal sealed class HairStyle
        {
            public string Name;
            public List<string> Pieces = new List<string>();
        }

        [MenuItem("Game/Characters/Review/Render Generated Outfit Sheet")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = Content(out List<GeneratedOutfit> outfits, out _);
            if (content == null) return;

            var generator = new CharacterGenerator(StandInContent.Load().LoadCatalog());
            var builder = new CharacterBuilder(content);
            Vector3[] positions = Femininity.Select((_, i) => new Vector3((i - 2.5f) * 1.0f, 0f, 0f)).ToArray();
            LightingRig.Day.Apply(positions);
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background, 26f);

            var rows = new List<Texture2D>();
            foreach (GeneratedOutfit outfit in outfits)
            {
                var characters = new List<ModularCharacter>();
                for (int i = 0; i < Femininity.Length; i++)
                {
                    CharacterDna dna = generator.Generate((ulong)(41 + i), new GenerationRequest { Archetype = "casual", FemininityMin = Femininity[i], FemininityMax = Femininity[i] });
                    dna.Hair.PieceIds = new List<string>();
                    dna.Outfits = new List<OutfitDna> { new OutfitDna { Context = "casual", Items = outfit.Items } };
                    ModularCharacter character = builder.Build(dna, "casual");
                    character.transform.position = positions[i];
                    Poses.Relaxed(character.transform);
                    foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                        renderer.forceMatrixRecalculationPerRender = true;
                    characters.Add(character);
                }
                rows.Add(Snapshot.Shoot(camera, new Vector3(0f, 0.95f, 7.4f), new Vector3(0f, 0.86f, 0f), 1540, 620));

                // Close up, the most feminine and the most masculine: front, three-quarter front, side, three-quarter back.
                var closeUps = new List<Texture2D>();
                foreach (ModularCharacter character in new[] { characters.First(), characters.Last() })
                {
                    foreach (ModularCharacter other in characters) other.gameObject.SetActive(other == character);
                    Vector3 target = character.transform.position + new Vector3(0f, 0.86f, 0f);
                    foreach (Vector3 direction in new[] { new Vector3(0f, 0.05f, 1f), new Vector3(0.7f, 0.05f, 0.7f), new Vector3(1f, 0.05f, 0f), new Vector3(-0.6f, 0.05f, -0.8f) })
                        closeUps.Add(Snapshot.Shoot(camera, target + direction.normalized * 3.6f, target, 192, 400));
                }
                rows.Add(Snapshot.Grid(closeUps, 8, Color.black));
                foreach (ModularCharacter character in characters) UnityEngine.Object.DestroyImmediate(character.gameObject);
            }
            string path = GeneratedLineup.Folder + "/generated-outfit.png";
            Snapshot.Save(Snapshot.StackVertically(rows, Color.black), path);
            Debug.Log($"[Review] Wrote {path}: {Femininity.Length} people on {GeneratedBodySheet.BodyPath} in " +
                      string.Join("; ", outfits.Select(o => $"{o.Name} = {string.Join(" + ", o.Items.Select(i => i.ItemId))}")));
        }

        /// <summary>
        /// A copy of the content with the generated body and only the generated parts (bound, with catalog entries made
        /// from their sidecars): the generated outfits, in name order, and the generated hairstyles. Null, with the
        /// reason logged, if the body or the garments are missing.
        /// </summary>
        internal static CharacterContent Content(out List<GeneratedOutfit> outfits, out List<HairStyle> hair)
        {
            outfits = new List<GeneratedOutfit>();
            hair = new List<HairStyle>();
            var body = AssetDatabase.LoadAssetAtPath<ModularCharacter>(GeneratedBodySheet.BodyPath);
            if (body == null)
            {
                Debug.LogError($"[Review] No generated body at {GeneratedBodySheet.BodyPath}");
                return null;
            }
            CharacterContent content = UnityEngine.Object.Instantiate(StandInContent.Load());
            content.BodyPrefab = body;
            content.Parts = new List<PartBinding>();
            ContentCatalog catalog = content.LoadCatalog();
            catalog.Parts.Clear(); // stand-in parts don't fit the generated skeleton

            foreach (string sidecarPath in Directory.GetFiles(Folder, "*.character.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                Sidecar sidecar = JsonUtility.FromJson<Sidecar>(File.ReadAllText(sidecarPath));
                if (sidecar.kind != "part") continue;
                string id = Path.GetFileName(sidecarPath).Replace(".character.json", "");
                var prefab = AssetDatabase.LoadAssetAtPath<CharacterPart>(Folder + id + ".fbx");
                if (prefab == null)
                {
                    Debug.LogError($"[Review] {id} has a sidecar but no imported part.");
                    continue;
                }
                content.Parts.Add(new PartBinding { Id = id, Prefab = prefab });
                if (sidecar.slot.StartsWith("hair.", StringComparison.Ordinal))
                {
                    catalog.Parts.Add(new PartEntry { Id = id, Slot = sidecar.slot, Regions = 3, Tags = sidecar.tags.ToList() });
                    HairStyle style = hair.FirstOrDefault(h => h.Name == sidecar.style);
                    if (style == null) hair.Add(style = new HairStyle { Name = sidecar.style ?? id });
                    style.Pieces.Add(id);
                    continue;
                }
                catalog.Parts.Add(new PartEntry { Id = id, Slot = sidecar.slot, Regions = sidecar.regions, Tags = new List<string> { "casual" } });
                GeneratedOutfit outfit = outfits.FirstOrDefault(o => o.Name == sidecar.outfit);
                if (outfit == null) outfits.Add(outfit = new GeneratedOutfit { Name = sidecar.outfit ?? id });
                outfit.Items.Add(new ItemDna { ItemId = id, RegionColors = sidecar.colours.Select(HsvColor.FromHex).ToList() });
            }
            outfits.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            if (outfits.Count == 0)
            {
                Debug.LogError($"[Review] No generated parts in {Folder}");
                return null;
            }
            return content;
        }
    }
}
