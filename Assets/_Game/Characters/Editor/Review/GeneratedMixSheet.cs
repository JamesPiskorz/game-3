using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// Every generated top over every generated bottom, as the generator mixes them (decision 0008): a row per top,
    /// a column per bottom, chest to knee from the front in the relaxed pose, each in its image's colours, a feminine
    /// and a masculine body in turn. Pieces are cut against one waist line (garment_tools.WAIST_ABOVE_HIPS), so no
    /// pairing should show a strip of midriff (Docs/review/generated-mix.png). Uses the generated content
    /// (<see cref="GeneratedContent"/>).
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.GeneratedMixSheet.Render
    /// </summary>
    public static class GeneratedMixSheet
    {
        [MenuItem("Game/Characters/Review/Render Generated Mix Sheet")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = GeneratedContent.Write();
            if (content == null) return;
            GeneratedOutfitSheet.Content(out List<GeneratedOutfitSheet.GeneratedOutfit> outfits, out _);
            ContentCatalog catalog = content.LoadCatalog();
            var colours = outfits.SelectMany(o => o.Items).ToDictionary(i => i.ItemId, i => i.RegionColors);
            List<PartEntry> tops = catalog.PartsInSlot(Slots.Top).ToList(), bottoms = catalog.PartsInSlot(Slots.Bottom).ToList();
            PartEntry shoes = catalog.PartsInSlot(Slots.Shoes).First();

            var generator = new CharacterGenerator(catalog);
            var builder = new CharacterBuilder(content);
            LightingRig.Day.Apply(new[] { Vector3.zero });
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background, 18f);
            var tiles = new List<Texture2D>();
            int n = 0;
            foreach (PartEntry top in tops)
                foreach (PartEntry bottom in bottoms)
                {
                    float femininity = n++ % 2 == 0 ? 1f : 0f;
                    CharacterDna dna = generator.Generate(41, new GenerationRequest { FemininityMin = femininity, FemininityMax = femininity });
                    dna.Hair.PieceIds = new List<string>();
                    dna.Outfits = new List<OutfitDna>
                    {
                        new OutfitDna { Context = "casual", Items = new[] { top, bottom, shoes }.Select(p => Item(p, colours)).ToList() },
                    };
                    ModularCharacter character = builder.Build(dna, "casual");
                    Poses.Relaxed(character.transform);
                    foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                        renderer.forceMatrixRecalculationPerRender = true;
                    tiles.Add(Snapshot.Shoot(camera, new Vector3(0f, 0.95f, 3.2f), new Vector3(0f, 0.9f, 0f), 260, 300));
                    Object.DestroyImmediate(character.gameObject);
                }
            string path = GeneratedLineup.Folder + "/generated-mix.png";
            Snapshot.Save(Snapshot.Grid(tiles, bottoms.Count, Color.black), path);
            Debug.Log($"[Review] Wrote {path}: rows {string.Join(", ", tops.Select(t => t.Id))}; columns {string.Join(", ", bottoms.Select(b => b.Id))}");
        }

        static ItemDna Item(PartEntry part, Dictionary<string, List<HsvColor>> colours) => new ItemDna
        {
            ItemId = part.Id,
            RegionColors = colours.TryGetValue(part.Id, out List<HsvColor> c) ? c.ToList() : Enumerable.Repeat(new HsvColor(0f, 0f, 0.7f), part.Regions).ToList(),
        };
    }
}
