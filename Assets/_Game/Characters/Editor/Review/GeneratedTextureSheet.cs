using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// The clothing texture library in the game's shader (decision 0008): every fabric (FabricTextures) worn as a
    /// dress and a shirt, and every print (PatternTextures) on a dress or a T-shirt in turn, each a close-up of the upper
    /// body. Writes Docs/review/generated-fabrics.png and generated-prints.png, tiles in library order
    /// (Tools/Textures/swatches.py labels them). Uses the generated content.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.GeneratedTextureSheet.Render
    /// </summary>
    public static class GeneratedTextureSheet
    {
        const string Dress = "cloth.onepiece.cocktail_gen01";
        const string Shirt = "cloth.top.tshirt_gen01";
        const string Trousers = "cloth.bottom.chinos_gen01";
        const string Shoes = "cloth.shoes.pumps_gen01";
        static readonly HsvColor[] Tints =
        {
            new HsvColor(0.62f, 0.55f, 0.62f), new HsvColor(0.98f, 0.7f, 0.72f), new HsvColor(0.12f, 0.12f, 0.92f),
            new HsvColor(0.0f, 0.0f, 0.22f), new HsvColor(0.1f, 0.6f, 0.86f), new HsvColor(0.35f, 0.45f, 0.5f),
        };

        [MenuItem("Game/Characters/Review/Render Generated Texture Sheets")]
        public static void Render()
        {
            RenderFabrics();
            RenderPrints();
        }

        public static void RenderFabrics()
        {
            Session(out CharacterContent content, out CharacterGenerator generator, out CharacterBuilder builder, out Camera camera);
            if (content == null) return;
            var tiles = new List<Texture2D>();
            List<FabricTextures.Fabric> fabrics = FabricTextures.Entries();
            for (int i = 0; i < fabrics.Count; i++)
            {
                Material fabric = FabricTextures.Material(fabrics[i].Id);
                HsvColor tint = Tints[i % Tints.Length];
                foreach (bool dress in new[] { true, false })
                {
                    ModularCharacter character = Dressed(generator, builder, dress
                        ? new[] { Item(Dress, tint), Item(Shoes, new HsvColor(0f, 0f, 0.15f)) }
                        : new[] { Item(Shirt, tint), Item(Trousers, Tints[(i + 3) % Tints.Length]), Item(Shoes, new HsvColor(0f, 0f, 0.15f)) });
                    if (fabric != null)
                        foreach (CharacterPart part in character.GetComponentsInChildren<CharacterPart>())
                            if (part.Slot != Slots.Shoes)
                                foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>())
                                    foreach (Material material in renderer.materials) Wear(material, fabric);
                    tiles.Add(CloseUp(camera));
                    UnityEngine.Object.DestroyImmediate(character.gameObject);
                }
            }
            string path = GeneratedLineup.Folder + "/generated-fabrics.png";
            Snapshot.Save(Snapshot.Grid(tiles, 8, Color.black), path);
            Debug.Log($"[Review] Wrote {path}: {fabrics.Count} fabrics, each as a dress and a shirt: {string.Join(", ", fabrics.Select(f => f.Id))}");
        }

        public static void RenderPrints()
        {
            Session(out CharacterContent content, out CharacterGenerator generator, out CharacterBuilder builder, out Camera camera);
            if (content == null) return;
            ContentCatalog catalog = content.LoadCatalog();
            var tiles = new List<Texture2D>();
            for (int i = 0; i < catalog.Patterns.Count; i++)
            {
                PatternEntry pattern = catalog.Patterns[i];
                HsvColor tint = Tints[i % Tints.Length];
                HsvColor ink = Tints[(i + 2) % Tints.Length];
                float scale = (pattern.MinScale + pattern.MaxScale) / 2f;
                bool dress = i % 2 == 0;
                ItemDna printed = Item(dress ? Dress : Shirt, tint);
                printed.PatternId = pattern.Id;
                printed.PatternScale = scale;
                printed.PatternColor = ink;
                ModularCharacter character = Dressed(generator, builder, dress
                    ? new[] { printed, Item(Shoes, new HsvColor(0f, 0f, 0.15f)) }
                    : new[] { printed, Item(Trousers, new HsvColor(0.6f, 0.25f, 0.3f)), Item(Shoes, new HsvColor(0f, 0f, 0.15f)) });
                tiles.Add(CloseUp(camera));
                UnityEngine.Object.DestroyImmediate(character.gameObject);
            }
            string path = GeneratedLineup.Folder + "/generated-prints.png";
            Snapshot.Save(Snapshot.Grid(tiles, 8, Color.black), path);
            Debug.Log($"[Review] Wrote {path}: {catalog.Patterns.Count} prints, on a dress and a T-shirt in turn: {string.Join(", ", catalog.Patterns.Select(p => p.Id))}");
        }

        static void Session(out CharacterContent content, out CharacterGenerator generator, out CharacterBuilder builder, out Camera camera)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            content = GeneratedContent.Load();
            generator = null;
            builder = null;
            camera = null;
            if (content == null)
            {
                Debug.LogError("[Review] No generated content: Game > Characters > Content > Write Generated Content.");
                return;
            }
            generator = new CharacterGenerator(content.LoadCatalog());
            builder = new CharacterBuilder(content);
            LightingRig.Day.Apply(new[] { Vector3.zero });
            camera = Snapshot.CreateCamera(LightingRig.Day.Background, 22f);
        }

        static ModularCharacter Dressed(CharacterGenerator generator, CharacterBuilder builder, IEnumerable<ItemDna> items)
        {
            CharacterDna dna = generator.Generate(41, new GenerationRequest { FemininityMin = 1f, FemininityMax = 1f });
            dna.Hair.PieceIds = new List<string>();
            dna.Outfits = new List<OutfitDna> { new OutfitDna { Context = "casual", Items = items.ToList() } };
            ModularCharacter character = builder.Build(dna, "casual");
            Poses.Relaxed(character.transform);
            foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                renderer.forceMatrixRecalculationPerRender = true;
            return character;
        }

        /// <summary>The torso close, three-quarter on, where the cloth's surface shows.</summary>
        static Texture2D CloseUp(Camera camera)
        {
            Vector3 target = new Vector3(0f, 1.12f, 0f);
            return Snapshot.Shoot(camera, target + new Vector3(0.55f, 0.05f, 1.35f), target, 300, 300);
        }

        static ItemDna Item(string id, HsvColor colour) => new ItemDna
        {
            ItemId = id,
            RegionColors = Enumerable.Repeat(colour, 3).ToList(),
        };

        /// <summary>A part's material wearing another fabric: its surface texture, tiling, gain and lift.</summary>
        static void Wear(Material material, Material fabric) => ToonMaterial.SetFabric(material, fabric);
    }
}
