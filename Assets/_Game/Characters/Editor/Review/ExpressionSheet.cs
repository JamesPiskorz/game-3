using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
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
    /// Every facial expression on a few generated faces, close up: one row per character, one column per expression
    /// (Docs/review/expressions.png). Expressions must read on every face shape the generator makes, not just one.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.ExpressionSheet.Render
    /// </summary>
    public static class ExpressionSheet
    {
        [MenuItem("Game/Characters/Review/Render Expression Sheet")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = StandInContent.Load();
            var generator = new CharacterGenerator(content.LoadCatalog());
            List<CharacterDna> people = new PopulationGenerator(generator).Generate(31, 4);
            var builder = new CharacterBuilder(content);
            Vector3[] positions = Enumerable.Range(0, people.Count).Select(i => new Vector3(i * 3f, 0f, 0f)).ToArray();
            LightingRig.Day.Apply(positions);
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background, 26f);

            bool blinking = FaceExpression.Blinking;
            FaceExpression.Blinking = false;
            var shots = new List<Texture2D>();
            for (int i = 0; i < people.Count; i++)
            {
                ModularCharacter character = builder.Build(people[i], "casual");
                character.transform.position = positions[i];
                Poses.Relaxed(character.transform);
                // Edit mode doesn't re-skin between back-to-back renders unless told to.
                foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                    renderer.forceMatrixRecalculationPerRender = true;
                var face = character.GetComponent<FaceExpression>();
                Transform head = character.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
                foreach (string expression in FaceExpression.Poses.Keys)
                {
                    face.Show(expression, 1f, float.PositiveInfinity);
                    face.Snap();
                    Vector3 target = head.TransformPoint(new Vector3(0f, 0.1f, 0f));
                    shots.Add(Snapshot.Shoot(camera, target + new Vector3(0.05f, 0.01f, 0.5f), target, 220, 220));
                }
            }
            FaceExpression.Blinking = blinking;

            Snapshot.Save(Snapshot.Grid(shots, FaceExpression.Poses.Count, Color.black), GeneratedLineup.Folder + "/expressions.png");
            Debug.Log("[Review] Expressions: " + string.Join(", ", FaceExpression.Poses.Keys));
        }
    }
}
