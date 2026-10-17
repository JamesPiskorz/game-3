using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.ContentAuthoring;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Modular;
using Game.Characters.Review;
using Game.Sim;
using Game.Sim.Residents;
using Game.Sim.Sandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Town
{
    /// <summary>
    /// Residents before and after their latest makeover, eight weeks into a town, to check by eye that a haircut,
    /// a dye job or new clothes reads as the same person changed. Each pair: as the town began, then now.
    /// Headless: -executeMethod Game.Town.MakeoverSheet.Render
    /// </summary>
    public static class MakeoverSheet
    {
        const int Pairs = 8, Tile = 220;
        const float Spacing = 4f;

        [MenuItem("Game/Town/Render Makeover Sheet")]
        public static void Render()
        {
            ContentCatalog characters = StandInContent.Load().LoadCatalog();
            SimCatalog sim = SimCatalog.FromJson(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Game/Sim/Content/SimCatalog.json").text);
            SimWorld start = new TownGenerator(sim, characters).Create(0x5EED, 24);
            SimWorld later = new TownGenerator(sim, characters).Create(0x5EED, 24);
            later.Now = SimTime.At(56, 9);
            Makeovers.CatchUp(later);
            List<Resident> changed = later.Residents.Where(r => r.LastMakeover != null).Take(Pairs).ToList();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var builder = new CharacterBuilder(StandInContent.Load());
            int count = changed.Count * 2;
            Vector3[] positions = Enumerable.Range(0, count).Select(i => new Vector3(i * Spacing, 0f, 0f)).ToArray();
            LightingRig.Day.Apply(positions);
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background);

            var tiles = new List<Texture2D>();
            for (int i = 0; i < changed.Count; i++)
            {
                Resident now = changed[i];
                foreach ((CharacterDna dna, int slot) in new[] { (start.Residents[now.Index].Dna, i * 2), (now.Dna, i * 2 + 1) })
                {
                    ModularCharacter character = builder.Build(dna, TownSession.DayOutfit);
                    character.transform.position = positions[slot];
                    Poses.Relaxed(character.transform);
                    Vector3 origin = positions[slot];
                    tiles.Add(Snapshot.Shoot(camera, origin + new Vector3(0f, 1.0f, 3.7f), origin + new Vector3(0f, 0.93f, 0f), Tile, Tile * 2));
                }
                Debug.Log($"[Review] {now.Name}: {now.LastMakeover.Kind} on day {now.LastMakeover.Day + 1}");
            }
            Snapshot.Save(Snapshot.Grid(tiles, 8, Color.black), $"{GeneratedLineup.Folder}/makeover-sheet.png");
            Debug.Log($"[Review] Rendered {changed.Count} makeovers to {GeneratedLineup.Folder}/makeover-sheet.png");
        }
    }
}
