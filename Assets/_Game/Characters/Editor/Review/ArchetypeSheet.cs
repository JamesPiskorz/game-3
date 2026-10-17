using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using Game.Characters.Rng;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// What each style archetype looks like today: one row per archetype, eight characters generated in it, in their
    /// everyday outfits. For art direction (style bible §9 leaves each archetype's look to art) and for checking that
    /// the styles read as different from one another.
    /// Headless: -executeMethod Game.Characters.Review.ArchetypeSheet.Render
    /// </summary>
    public static class ArchetypeSheet
    {
        const int PerStyle = 8, Tile = 180;
        const float Spacing = 4f;

        [MenuItem("Game/Characters/Review/Render Archetype Sheet")]
        public static void Render()
        {
            var generator = new CharacterGenerator(StandInContent.Load().LoadCatalog());
            List<StyleArchetype> styles = generator.Catalog.Archetypes;
            var people = new List<CharacterDna>();
            foreach (StyleArchetype style in styles)
                for (int i = 0; i < PerStyle; i++)
                    people.Add(generator.Generate(StableHash.SplitMix64(0x57713UL + (ulong)(people.Count * 7919)), new GenerationRequest { Archetype = style.Id }));

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var builder = new CharacterBuilder(StandInContent.Load());
            Vector3[] positions = Enumerable.Range(0, people.Count).Select(i => new Vector3(i * Spacing, 0f, 0f)).ToArray();
            LightingRig.Day.Apply(positions);
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background);

            var tiles = new List<Texture2D>();
            for (int i = 0; i < people.Count; i++)
            {
                ModularCharacter character = builder.Build(people[i], "casual");
                character.transform.position = positions[i];
                Poses.Relaxed(character.transform);
                tiles.Add(Snapshot.Shoot(camera, positions[i] + new Vector3(0f, 1.0f, 3.7f), positions[i] + new Vector3(0f, 0.93f, 0f), Tile, Tile * 2));
            }
            Snapshot.Save(Snapshot.Grid(tiles, PerStyle, Color.black), $"{GeneratedLineup.Folder}/archetype-sheet.png");
            Debug.Log($"[Review] Archetype sheet: rows are {string.Join(", ", styles.Select(s => s.Name ?? s.Id))}");
        }
    }
}
