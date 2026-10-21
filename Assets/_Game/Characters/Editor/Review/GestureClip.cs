using System.Collections.Generic;
using System.IO;
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
    /// A generated character animated as the game animates it (IdleMotion breathing, swaying and looking about, the
    /// face blinking), waving and then smiling, stepped at 30 fps: every frame to Recordings/wave-smile/ for a video,
    /// and a filmstrip to Docs/review/wave-smile.png. Built like <see cref="GeneratedOutfitSheet"/>: the generated body
    /// in the first generated outfit.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.GestureClip.Render
    /// </summary>
    public static class GestureClip
    {
        const string FramesFolder = "Recordings/wave-smile";
        const float FrameSeconds = 1f / 30f;
        const float Seconds = 5.2f;
        const float WaveAt = 0.5f;
        const float SmileAt = 2.9f;
        static readonly float[] Filmstrip = { 0.3f, 0.9f, 1.15f, 1.4f, 1.65f, 2.6f, 3.4f, 4.6f };

        [MenuItem("Game/Characters/Review/Render Wave And Smile Clip")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = GeneratedOutfitSheet.Content(out List<GeneratedOutfitSheet.GeneratedOutfit> outfits, out _);
            if (content == null) return;

            CharacterDna dna = new CharacterGenerator(StandInContent.Load().LoadCatalog())
                .Generate(41, new GenerationRequest { Archetype = "casual", FemininityMin = 1f, FemininityMax = 1f });
            dna.Hair.PieceIds = new List<string>(); // no generated hair yet
            dna.Outfits = new List<OutfitDna> { new OutfitDna { Context = "casual", Items = outfits[0].Items } };
            ModularCharacter character = new CharacterBuilder(content).Build(dna, "casual");
            Poses.Relaxed(character.transform);
            foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                renderer.forceMatrixRecalculationPerRender = true;
            var idle = character.GetComponent<IdleMotion>();
            var face = character.GetComponent<FaceExpression>();
            idle.Rebase(); // the relaxed arms are the base the idle and the wave layer over
            idle.Wave(WaveAt);

            LightingRig.Day.Apply(new[] { Vector3.zero });
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background, 26f);
            Directory.CreateDirectory(FramesFolder);
            foreach (string old in Directory.GetFiles(FramesFolder, "frame_*.png")) File.Delete(old);

            var strip = new List<Texture2D>();
            int frames = Mathf.RoundToInt(Seconds / FrameSeconds);
            bool smiling = false;
            for (int frame = 0; frame <= frames; frame++)
            {
                float t = frame * FrameSeconds;
                if (!smiling && t >= SmileAt)
                {
                    face.Show("happy", 1f, float.PositiveInfinity);
                    smiling = true;
                }
                idle.Pose(t);
                face.Tick(t, frame == 0 ? 0f : FrameSeconds);

                Texture2D shot = Snapshot.Shoot(camera, new Vector3(0.4f, 1.5f, 2.3f), new Vector3(0.12f, 1.47f, 0f), 720, 900);
                File.WriteAllBytes($"{FramesFolder}/frame_{frame:000}.png", shot.EncodeToPNG());
                if (System.Array.Exists(Filmstrip, s => Mathf.RoundToInt(s / FrameSeconds) == frame))
                    strip.Add(Snapshot.Shoot(camera, new Vector3(0.4f, 1.5f, 2.3f), new Vector3(0.12f, 1.47f, 0f), 240, 300));
                Object.DestroyImmediate(shot);
            }

            string path = GeneratedLineup.Folder + "/wave-smile.png";
            Snapshot.Save(Snapshot.Grid(strip, strip.Count, Color.black), path);
            Debug.Log($"[Review] Wrote {frames + 1} frames to {FramesFolder} and the filmstrip {path}: wave at {WaveAt} s, smile at {SmileAt} s");
        }
    }
}
