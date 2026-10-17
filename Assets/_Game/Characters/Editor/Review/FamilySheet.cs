using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
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
    /// Families side by side, to judge <see cref="FamilyResemblance"/> by eye: each block is one family of three,
    /// full bodies over faces. They should read as related without looking like copies.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.FamilySheet.Render
    /// </summary>
    public static class FamilySheet
    {
        const int Families = 6, Members = 3, Columns = 3, Tile = 200, Gap = 40;
        const float Spacing = 4f;

        [MenuItem("Game/Characters/Review/Render Family Sheet")]
        public static void Render()
        {
            var generator = new CharacterGenerator(StandInContent.Load().LoadCatalog());
            var people = new List<CharacterDna>();
            for (int f = 0; f < Families; f++)
            {
                CharacterDna founder = generator.Generate(StableHash.SplitMix64(0xFA41UL + (ulong)f));
                people.Add(founder);
                for (int m = 1; m < Members; m++)
                    people.Add(generator.GenerateRelative(founder, StableHash.SplitMix64(0xFA41UL + (ulong)(f * 16 + m) * 7919UL)));
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var builder = new CharacterBuilder(StandInContent.Load());
            Vector3[] positions = Enumerable.Range(0, people.Count).Select(i => new Vector3(i * Spacing, 0f, 0f)).ToArray();
            LightingRig.Day.Apply(positions);
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background);

            var blocks = new List<Texture2D>();
            for (int f = 0; f < Families; f++)
            {
                var bodies = new List<Texture2D>();
                var faces = new List<Texture2D>();
                for (int m = 0; m < Members; m++)
                {
                    int i = f * Members + m;
                    ModularCharacter character = builder.Build(people[i], "casual");
                    character.transform.position = positions[i];
                    Poses.Relaxed(character.transform);
                    Vector3 origin = positions[i];
                    bodies.Add(Snapshot.Shoot(camera, origin + new Vector3(0f, 1.0f, 3.7f), origin + new Vector3(0f, 0.93f, 0f), Tile, Tile * 2));
                    Vector3 head = character.GetComponentsInChildren<Transform>().First(t => t.name == "Head").TransformPoint(new Vector3(0f, 0.1f, 0f));
                    faces.Add(Snapshot.Shoot(camera, head + new Vector3(0.1f, 0.02f, 0.62f), head, Tile, Tile));
                }
                blocks.Add(Pad(Snapshot.StackVertically(new[] { Snapshot.Grid(bodies, Members, Color.black), Snapshot.Grid(faces, Members, Color.black) }, Color.black), Gap));
            }

            Snapshot.Save(Snapshot.Grid(blocks, Columns, Color.black), $"{GeneratedLineup.Folder}/family-sheet.png");
            Debug.Log($"[Review] Rendered {Families} families of {Members} to {GeneratedLineup.Folder}/family-sheet.png");
        }

        /// <summary>A black margin on the right and bottom, so neighbouring families don't run together.</summary>
        static Texture2D Pad(Texture2D image, int gap)
        {
            var padded = new Texture2D(image.width + gap, image.height + gap, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            var fill = new Color32[padded.width * padded.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(0, 0, 0, 255);
            padded.SetPixels32(fill);
            padded.SetPixels(0, gap, image.width, image.height, image.GetPixels());
            padded.Apply();
            return padded;
        }
    }
}
