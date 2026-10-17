using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using Game.Characters.Springs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// Spring-bone review: characters with tailed hair whip round 110°, simulated at 30 fps, filmed from behind at
    /// set moments (Docs/review/springs.png, reading order = time).
    /// unity run . -- -executeMethod Game.Characters.Review.SpringFilmstrip.Render
    /// </summary>
    public static class SpringFilmstrip
    {
        const string OutputPath = "Docs/review/springs.png";
        const float FrameSeconds = 1f / 30f;
        const float TurnSeconds = 0.25f;
        const float TurnDegrees = 110f;
        static readonly int[] Shots = { 0, 3, 5, 8, 11, 15, 22, 45 };
        static readonly string[] Hair = { "hair.back.ponytail_01", "hair.back.twintails_01" };

        [MenuItem("Game/Characters/Review/Render Spring Filmstrip")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = StandInContent.Load();
            var generator = new CharacterGenerator(content.LoadCatalog());
            var builder = new CharacterBuilder(content);
            LightingRig.Day.Apply(new[] { Vector3.zero });

            var characters = new List<ModularCharacter>();
            for (int i = 0; i < Hair.Length; i++)
            {
                CharacterDna dna = generator.Generate(100 + (ulong)i);
                dna.Hair.PieceIds = new List<string> { Hair[i] };
                ModularCharacter character = builder.Build(dna, "casual");
                character.transform.position = new Vector3((i - 0.5f) * 1.3f, 0f, 0f);
                Poses.Relaxed(character.transform);
                // Edit-mode renders back to back don't re-skin meshes on their own.
                foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                    renderer.forceMatrixRecalculationPerRender = true;
                characters.Add(character);
            }

            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background);
            var frames = new List<Texture2D>();
            for (int frame = 0; frame <= Shots.Max(); frame++)
            {
                float t = frame * FrameSeconds;
                float yaw = TurnDegrees * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / TurnSeconds));
                foreach (ModularCharacter character in characters)
                {
                    character.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    foreach (SpringBoneChain chain in character.GetComponents<SpringBoneChain>())
                        chain.Advance(FrameSeconds);
                }
                if (Shots.Contains(frame))
                    frames.Add(Snapshot.Shoot(camera, new Vector3(0f, 1.55f, -3.4f), new Vector3(0f, 1.32f, 0f), 600, 420));
            }

            Snapshot.Save(Snapshot.Grid(frames, 4, Color.black), OutputPath);
            Debug.Log($"[Review] Wrote {OutputPath}");
        }
    }
}
