using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Game.Characters.Building;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using Game.Characters.Springs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.Characters.Review
{
    /// <summary>
    /// Town-scale cost of the character system (PLAN.md §3.10): builds a town of generated residents and times
    /// generation, building, re-applying DNA, spring simulation and rendering, with renderer statistics. Writes
    /// Docs/review/perf.md. Editor timings on this machine are indicative, not a min-spec measurement.
    /// unity run . -- -executeMethod Game.Characters.Review.TownBenchmark.Run
    /// </summary>
    public static class TownBenchmark
    {
        const int Residents = 40;
        const string ReportPath = "Docs/review/perf.md";

        [MenuItem("Game/Characters/Review/Run Town Benchmark")]
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = StandInContent.Load();
            var generator = new CharacterGenerator(content.LoadCatalog());
            var builder = new CharacterBuilder(content);
            LightingRig.Day.Apply(Array.Empty<Vector3>());
            var report = new StringBuilder();
            report.AppendLine("# Town benchmark").AppendLine();
            report.AppendLine($"_{DateTime.Now:yyyy-MM-dd HH:mm} · {SystemInfo.processorType} · {SystemInfo.graphicsDeviceName} · Unity {Application.unityVersion} (editor, batch mode)_").AppendLine();
            report.AppendLine("Editor timings are indicative only: the editor is slower than a player build, and this is not min-spec hardware.").AppendLine();

            // Warm up: first-use costs (shader variants, asset loads) aren't per-character costs.
            UnityEngine.Object.DestroyImmediate(builder.Build(generator.Generate(999)).gameObject);

            var clock = Stopwatch.StartNew();
            List<CharacterDna> town = new PopulationGenerator(generator).Generate(2026, Residents);
            double generateMs = clock.Elapsed.TotalMilliseconds;

            var buildTimes = new List<double>();
            var characters = new List<ModularCharacter>();
            for (int i = 0; i < Residents; i++)
            {
                clock.Restart();
                ModularCharacter character = builder.Build(town[i], "casual");
                buildTimes.Add(clock.Elapsed.TotalMilliseconds);
                character.transform.position = new Vector3((i % 10) * 0.9f - 4f, 0f, (i / 10) * 1.4f);
                character.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                Poses.Relaxed(character.transform);
                characters.Add(character);
            }

            var applyTimes = new List<double>();
            for (int i = 0; i < Residents; i++)
            {
                town[i].Body.Sliders["body.weight"] = 0.5f;
                clock.Restart();
                builder.Apply(characters[i], town[i], "casual");
                applyTimes.Add(clock.Elapsed.TotalMilliseconds);
            }
            var contextTimes = new List<double>();
            for (int i = 0; i < Residents; i++)
            {
                clock.Restart();
                builder.Apply(characters[i], town[i], "date");
                contextTimes.Add(clock.Elapsed.TotalMilliseconds);
            }

            List<SpringBoneChain> chains = characters.SelectMany(c => c.GetComponents<SpringBoneChain>()).ToList();
            const int SpringFrames = 600;
            clock.Restart();
            for (int frame = 0; frame < SpringFrames; frame++)
                foreach (SpringBoneChain chain in chains)
                    chain.Advance(1f / 60f);
            double springMsPerFrame = clock.Elapsed.TotalMilliseconds / SpringFrames;

            int renderers = characters.Sum(c => c.GetComponentsInChildren<Renderer>().Length);
            int vertices = characters.Sum(c => c.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(r => r.sharedMesh.vertexCount));
            int blendShapes = characters.Sum(c => c.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(r => r.sharedMesh.blendShapeCount));
            int bones = characters.Sum(c => c.GetComponentsInChildren<Transform>().Length);

            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background, 40f);
            camera.transform.position = new Vector3(0f, 2.4f, -6.5f);
            camera.transform.LookAt(new Vector3(0f, 0.9f, 2f));
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            double RenderMs()
            {
                for (int i = 0; i < 5; i++) camera.Render();
                const int Frames = 60;
                var timer = Stopwatch.StartNew();
                for (int i = 0; i < Frames; i++)
                {
                    camera.Render();
                    RenderTexture.active = target; // read a pixel back so the GPU work is included
                    readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                    RenderTexture.active = null;
                }
                return timer.Elapsed.TotalMilliseconds / Frames;
            }

            List<SkinnedMeshRenderer> skinned = characters.SelectMany(c => c.GetComponentsInChildren<SkinnedMeshRenderer>()).ToList();
            List<Material> materials = skinned.SelectMany(r => r.sharedMaterials).Distinct().ToList();
            Light sun = UnityEngine.Object.FindAnyObjectByType<Light>();

            double renderMs = RenderMs();
            // What an identity bake would save: the same characters with every blendshape at zero.
            var weights = skinned.Select(r => Enumerable.Range(0, r.sharedMesh.blendShapeCount).Select(r.GetBlendShapeWeight).ToArray()).ToList();
            foreach (SkinnedMeshRenderer renderer in skinned)
                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++) renderer.SetBlendShapeWeight(i, 0f);
            double noShapesMs = RenderMs();
            for (int r = 0; r < skinned.Count; r++)
                for (int i = 0; i < weights[r].Length; i++) skinned[r].SetBlendShapeWeight(i, weights[r][i]);
            foreach (Material material in materials) material.SetShaderPassEnabled("SRPDefaultUnlit", false);
            double noOutlineMs = RenderMs();
            foreach (Material material in materials) material.SetShaderPassEnabled("SRPDefaultUnlit", true);
            LightShadows shadows = sun.shadows;
            sun.shadows = LightShadows.None;
            double noShadowMs = RenderMs();
            sun.shadows = shadows;
            // A street view: the same town seen from far enough that most residents drop to the low detail level.
            camera.transform.position = new Vector3(0f, 4f, -22f);
            camera.transform.LookAt(new Vector3(0f, 0.9f, 2f));
            double farMs = RenderMs();
            foreach (ModularCharacter character in characters) character.GetComponent<CharacterLod>()?.Apply(Vector3.Distance(camera.transform.position, character.transform.position));
            double farLodMs = RenderMs();
            foreach (ModularCharacter character in characters) character.gameObject.SetActive(false);
            double emptyMs = RenderMs();
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(target);

            report.AppendLine($"Town of **{Residents}** generated residents (world seed 2026), stand-in content.").AppendLine();
            report.AppendLine("| Measure | Value |").AppendLine("|---|---|");
            report.AppendLine($"| Generate the town (DNA, with lookalike rejection) | {generateMs:0.0} ms total |");
            report.AppendLine($"| Build a character (median / max) | {Median(buildTimes):0.00} / {buildTimes.Max():0.00} ms |");
            report.AppendLine($"| Re-apply after a slider change (median) | {Median(applyTimes):0.00} ms |");
            report.AppendLine($"| Re-apply for another outfit context (median) | {Median(contextTimes):0.00} ms |");
            report.AppendLine($"| Spring bones, whole town, per 60 Hz frame | {springMsPerFrame:0.000} ms ({chains.Count} chains) |");
            report.AppendLine($"| Render the town, 1920×1080, CPU + GPU | {renderMs:0.00} ms |");
            report.AppendLine($"| … the empty scene (camera, floorless) | {emptyMs:0.00} ms |");
            report.AppendLine($"| … with every blendshape at zero (an identity bake's ceiling) | {noShapesMs:0.00} ms |");
            report.AppendLine($"| … without the outline pass | {noOutlineMs:0.00} ms |");
            report.AppendLine($"| … without sun shadows | {noShadowMs:0.00} ms |");
            report.AppendLine($"| Same town from 22 m, full detail | {farMs:0.00} ms |");
            report.AppendLine($"| Same town from 22 m, with CharacterLod | {farLodMs:0.00} ms |");
            report.AppendLine($"| Renderers / skinned vertices / blendshapes | {renderers} / {vertices:N0} / {blendShapes} |");
            report.AppendLine($"| Transforms under characters | {bones:N0} |");
            report.AppendLine();

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log("[Benchmark]\n" + report);
        }

        static double Median(List<double> values)
        {
            List<double> sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }
    }
}
