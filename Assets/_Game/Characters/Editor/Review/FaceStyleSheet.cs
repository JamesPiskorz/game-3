using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Expressions;
using Game.Characters.Generation;
using Game.Characters.Modular;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// Every eye style, then every brow style, on the same few faces, close up (Docs/review/face-styles.png): one row
    /// per face, one column per style. The styles should be told apart at a glance; the face presets and sliders vary
    /// within a style, and the style is what reads across the room. RenderGenerated does the same on the generated body
    /// (Docs/review/generated-face-styles.png).
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.FaceStyleSheet.Render
    /// </summary>
    public static class FaceStyleSheet
    {
        [MenuItem("Game/Characters/Review/Render Face Style Sheet")]
        public static void Render() => Render(StandInContent.Load, "face-styles.png");

        [MenuItem("Game/Characters/Review/Render Generated Face Style Sheet")]
        public static void RenderGenerated() => Render(GeneratedBodySheet.Content, "generated-face-styles.png");

        /// <summary>Loads the content after opening the scene: a new scene unloads assets nothing in it refers to.</summary>
        static void Render(System.Func<CharacterContent> load, string file)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = load();
            if (content == null) return;
            ContentCatalog catalog = content.LoadCatalog();
            var generator = new CharacterGenerator(catalog);
            List<CharacterDna> people = new PopulationGenerator(generator).Generate(57, 3);
            var builder = new CharacterBuilder(content);
            Vector3[] positions = Enumerable.Range(0, people.Count).Select(i => new Vector3(i * 3f, 0f, 0f)).ToArray();
            LightingRig.Day.Apply(positions);
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background, 26f);

            bool blinking = FaceExpression.Blinking;
            FaceExpression.Blinking = false;
            var columns = catalog.EyeStyles.Select(s => (Eye: s.Id, Brow: (string)null))
                .Concat(catalog.BrowStyles.Select(s => (Eye: (string)null, Brow: s.Id))).ToList();
            var shots = new List<Texture2D>();
            for (int i = 0; i < people.Count; i++)
            {
                ModularCharacter character = builder.Build(people[i], "casual");
                character.transform.position = positions[i];
                foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                    renderer.forceMatrixRecalculationPerRender = true; // edit mode doesn't re-skin between renders otherwise
                foreach ((string eye, string brow) in columns)
                {
                    CharacterDna styled = DnaSerializer.FromJson(DnaSerializer.ToJson(people[i], indented: false));
                    if (eye != null) styled.Face.EyeStyleId = eye;
                    if (brow != null) styled.Face.BrowStyleId = brow;
                    // Bangs would hide the brows: show the forehead.
                    styled.Hair.PieceIds.RemoveAll(id => catalog.Part(id)?.Slot == Slots.HairFront);
                    builder.Apply(character, styled, "casual");
                    Poses.Relaxed(character.transform);
                    var face = character.GetComponent<FaceExpression>();
                    face.Show("neutral", 1f, float.PositiveInfinity);
                    face.Snap();
                    Transform head = character.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
                    Vector3 target = head.TransformPoint(new Vector3(0f, 0.12f, 0f));
                    shots.Add(Snapshot.Shoot(camera, target + new Vector3(0f, 0f, 0.62f), target, 200, 200));
                }
            }
            FaceExpression.Blinking = blinking;

            Snapshot.Save(Snapshot.Grid(shots, columns.Count, Color.black), GeneratedLineup.Folder + "/" + file);
            Debug.Log("[Review] Face styles: " + string.Join(", ", columns.Select(c => c.Eye ?? c.Brow)));
        }
    }
}
