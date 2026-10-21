using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Expressions;
using Game.Characters.Generation;
using Game.Characters.Import;
using Game.Characters.Modular;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// The generated body (Tools/Blender/make_generated_body.py, decision 0008) as the game builds it, before it is
    /// canonical: generated people on it, undressed (the stand-in parts don't fit its skeleton), then every expression
    /// close up on two of them (Docs/review/generated-body.png). Uses a copy of the content with only the body swapped.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.GeneratedBodySheet.Render
    /// </summary>
    public static class GeneratedBodySheet
    {
        public const string BodyPath = CharacterModelImporter.Folder + "Generated/GeneratedBody.fbx";

        [MenuItem("Game/Characters/Review/Render Generated Body Sheet")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = Content();
            if (content == null) return;

            var generator = new CharacterGenerator(content.LoadCatalog());
            List<CharacterDna> people = new PopulationGenerator(generator).Generate(31, 6);
            var builder = new CharacterBuilder(content);
            Vector3[] positions = Enumerable.Range(0, people.Count).Select(i => new Vector3((i - 2.5f) * 1.0f, 0f, 0f)).ToArray();
            LightingRig.Day.Apply(positions);
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background, 26f);

            bool blinking = FaceExpression.Blinking;
            FaceExpression.Blinking = false;
            var characters = new List<ModularCharacter>();
            for (int i = 0; i < people.Count; i++)
            {
                ModularCharacter character = builder.Build(people[i], "casual");
                character.transform.position = positions[i];
                Poses.Relaxed(character.transform);
                foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                    renderer.forceMatrixRecalculationPerRender = true;
                characters.Add(character);
            }

            var rows = new List<Texture2D>
            {
                Snapshot.Shoot(camera, new Vector3(0f, 1.0f, 10.5f), new Vector3(0f, 0.86f, 0f), 1540, 800),
            };
            var faces = new List<Texture2D>();
            foreach (ModularCharacter character in characters.Take(2))
            {
                var face = character.GetComponent<FaceExpression>();
                Transform head = character.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
                foreach (string expression in FaceExpression.Poses.Keys)
                {
                    face.Show(expression, 1f, float.PositiveInfinity);
                    face.Snap();
                    Vector3 target = head.TransformPoint(new Vector3(0f, 0.11f, 0f));
                    faces.Add(Snapshot.Shoot(camera, target + new Vector3(0.03f, 0.01f, 0.62f), target, 220, 220));
                }
                face.Show("neutral", 1f, float.PositiveInfinity);
                face.Snap();
            }
            FaceExpression.Blinking = blinking;
            rows.Add(Snapshot.Grid(faces, FaceExpression.Poses.Count, Color.black));

            string path = GeneratedLineup.Folder + "/generated-body.png";
            Snapshot.Save(Snapshot.StackVertically(rows, Color.black), path);
            Debug.Log($"[Review] Wrote {path}: {people.Count} people on {BodyPath}; expressions " + string.Join(", ", FaceExpression.Poses.Keys));
        }

        /// <summary>
        /// A copy of the content with only the body swapped for the generated one, and no parts (the stand-in parts
        /// don't fit its skeleton). Null, with the reason logged, if there's no generated body.
        /// </summary>
        internal static CharacterContent Content()
        {
            var body = AssetDatabase.LoadAssetAtPath<ModularCharacter>(BodyPath);
            if (body == null)
            {
                Debug.LogError($"[Review] No generated body at {BodyPath}");
                return null;
            }
            CharacterContent content = Object.Instantiate(StandInContent.Load());
            content.BodyPrefab = body;
            content.Parts = new List<PartBinding>();
            return content;
        }
    }
}
