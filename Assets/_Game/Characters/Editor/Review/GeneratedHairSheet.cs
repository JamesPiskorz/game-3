using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
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
    /// The generated hairstyles (Tools/Blender/make_generated_hair.py, decision 0008) on the generated body as the game
    /// builds it, in the generated outfit: one row per style, its front and back pieces worn together, head and
    /// shoulders from the front, three-quarter, side and back, on a feminine and a masculine person with the
    /// generator's hair colours (Docs/review/generated-hair.png). Uses <see cref="GeneratedOutfitSheet"/>'s content.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.GeneratedHairSheet.Render
    /// </summary>
    public static class GeneratedHairSheet
    {
        static readonly float[] Femininity = { 1f, 0f };
        static readonly Vector3[] Views =
        {
            new Vector3(0f, 0.08f, 1f), new Vector3(0.75f, 0.12f, 0.65f), new Vector3(1f, 0.05f, 0f), new Vector3(-0.35f, 0.1f, -0.94f),
        };

        [MenuItem("Game/Characters/Review/Render Generated Hair Sheet")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = GeneratedOutfitSheet.Content(out List<ItemDna> items, out List<GeneratedOutfitSheet.HairStyle> styles);
            if (content == null) return;
            if (styles.Count == 0)
            {
                Debug.LogError("[Review] No generated hair yet.");
                return;
            }

            var generator = new CharacterGenerator(StandInContent.Load().LoadCatalog());
            var builder = new CharacterBuilder(content);
            LightingRig.Day.Apply(new[] { Vector3.zero });
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background, 26f);

            var shots = new List<Texture2D>();
            for (int row = 0; row < styles.Count; row++)
            {
                for (int person = 0; person < Femininity.Length; person++)
                {
                    CharacterDna dna = generator.Generate((ulong)(61 + 7 * row + person), new GenerationRequest { Archetype = "casual", FemininityMin = Femininity[person], FemininityMax = Femininity[person], FantasyHair = false });
                    dna.Hair.PieceIds = styles[row].Pieces.ToList();
                    dna.Outfits = new List<OutfitDna> { new OutfitDna { Context = "casual", Items = items } };
                    ModularCharacter character = builder.Build(dna, "casual");
                    Poses.Relaxed(character.transform);
                    foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                        renderer.forceMatrixRecalculationPerRender = true;
                    Vector3 target = new Vector3(0f, 1.43f, 0f);
                    foreach (Vector3 direction in Views)
                        shots.Add(Snapshot.Shoot(camera, target + direction.normalized * 1.55f, target, 240, 280));
                    Object.DestroyImmediate(character.gameObject);
                }
            }

            string path = GeneratedLineup.Folder + "/generated-hair.png";
            Snapshot.Save(Snapshot.Grid(shots, Views.Length * Femininity.Length, Color.black), path);
            Debug.Log($"[Review] Wrote {path}: " + string.Join("; ", styles.Select(s => $"{s.Name} = {string.Join(" + ", s.Pieces)}")));
        }
    }
}
