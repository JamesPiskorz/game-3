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
    /// The generator's main review artifact (PLAN.md §3.8): a town of generated residents built with the real
    /// builder, rendered as a full-body contact sheet and a face sheet per outfit context, under day light.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.GeneratedLineup.Render
    /// </summary>
    public static class GeneratedLineup
    {
        public const string Folder = "Docs/review";
        public const ulong DefaultWorldSeed = 2026;
        const int Count = 32;
        const int Columns = 8;
        const float Spacing = 4f; // far enough apart that each tile shows one character

        public static void RegenerateContentAndRender()
        {
            StandInContent.RegenerateAll();
            Render();
        }

        [MenuItem("Game/Characters/Review/Render Generated Lineup")]
        public static void Render() => Render(DefaultWorldSeed);

        public static void Render(ulong worldSeed)
        {
            var generator = new CharacterGenerator(StandInContent.Load().LoadCatalog());
            List<CharacterDna> residents = new PopulationGenerator(generator).Generate(worldSeed, Count);

            foreach (string context in generator.Catalog.Contexts.Select(c => c.Id))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // NewScene unloads unused assets, so load content after it.
                var builder = new CharacterBuilder(StandInContent.Load());
                Vector3[] positions = Enumerable.Range(0, Count).Select(i => new Vector3(i * Spacing, 0f, 0f)).ToArray();
                LightingRig.Day.Apply(positions);

                var characters = new List<ModularCharacter>();
                for (int i = 0; i < Count; i++)
                {
                    ModularCharacter character = builder.Build(residents[i], context);
                    character.transform.position = positions[i];
                    Poses.Relaxed(character.transform);
                    characters.Add(character);
                }

                Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background);
                var bodies = new List<Texture2D>();
                var faces = new List<Texture2D>();
                foreach (ModularCharacter character in characters)
                {
                    Vector3 origin = character.transform.position;
                    bodies.Add(Snapshot.Shoot(camera, origin + new Vector3(0f, 1.0f, 3.7f), origin + new Vector3(0f, 0.93f, 0f), 300, 600));
                    Vector3 head = HeadPosition(character);
                    faces.Add(Snapshot.Shoot(camera, head + new Vector3(0.1f, 0.02f, 0.62f), head, 260, 260));
                }

                Snapshot.Save(Snapshot.Grid(bodies, Columns, Color.black), $"{Folder}/lineup-{context}.png");
                Snapshot.Save(Snapshot.Grid(faces, Columns, Color.black), $"{Folder}/faces-{context}.png");
            }
            Debug.Log($"[Review] Rendered {Count} generated residents (world seed {worldSeed}) to {Folder}");
        }

        static Vector3 HeadPosition(ModularCharacter character)
        {
            Transform head = character.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
            return head.TransformPoint(new Vector3(0f, 0.1f, 0f));
        }
    }
}
