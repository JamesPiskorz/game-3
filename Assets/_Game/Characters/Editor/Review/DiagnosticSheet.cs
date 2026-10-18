using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Game.Characters.Review
{
    /// <summary>
    /// The diagnostic set for every part (graphics plan V1), one sheet per part in Docs/review/diagnostics/ (not in
    /// git: regenerate on demand). Row 1: orthographic front, side and top, a three-quarter beauty shot, the
    /// silhouette and a close-up. Row 2: wireframe, mesh normals, outline normals (the tangents), and the first three
    /// light-turntable steps. Row 3: the rest of the light turntable, the sun stepped round in 45° increments. Beauty
    /// shots render as the game does (post on); debug views render without post, so their colours are exact.
    /// Headless: -executeMethod Game.Characters.Review.DiagnosticSheet.Render [-parts id,id,...]
    /// </summary>
    public static class DiagnosticSheet
    {
        public const string Folder = GeneratedLineup.Folder + "/diagnostics";
        const int TileWidth = 200, TileHeight = 260, Columns = 6, TurntableSteps = 8;
        static readonly Color Backdrop = LightingRig.Day.Background;

        [MenuItem("Game/Characters/Review/Render Diagnostic Sheets")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = StandInContent.Load();
            ContentCatalog catalog = content.LoadCatalog();
            var builder = new CharacterBuilder(content);
            CharacterDna model = new CharacterGenerator(catalog).Generate(11, new GenerationRequest { Archetype = "casual", FemininityMin = 0.45f, FemininityMax = 0.55f });
            LightingRig.Day.Apply(new[] { Vector3.zero });
            Light sun = RenderSettings.sun;
            Camera camera = Snapshot.CreateCamera(Backdrop);
            Shader diagnostics = Shader.Find("Hidden/Game/Diagnostics");
            var wireMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.12f, 0.12f, 0.16f) };

            HashSet<string> only = PartsArgument();
            List<PartEntry> parts = catalog.Parts.Where(p => only == null || only.Contains(p.Id))
                .OrderBy(p => p.Slot, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
            foreach (PartEntry part in parts)
            {
                ModularCharacter character = builder.Build(PartSheet.Wearing(model, part, catalog), "casual");
                Poses.Relaxed(character.transform);
                bool head = IsHeadPart(part);
                Vector3 focus = head ? new Vector3(0f, 1.62f, 0f) : new Vector3(0f, 0.93f, 0f);
                float size = head ? 0.24f : 1.02f; // orthographic half-height

                var tiles = new List<Texture2D>
                {
                    Orthographic(camera, focus, size, Vector3.forward, Vector3.up),
                    Orthographic(camera, focus, size, Vector3.right, Vector3.up),
                    Orthographic(camera, focus, size, Vector3.up, Vector3.back),
                    Perspective(camera, focus, head),
                    DebugView(camera, character, diagnostics, 2f, Color.black, Color.white, () => Orthographic(camera, focus, size, Vector3.forward, Vector3.up)),
                    CloseUp(camera, focus, head),
                    Wireframe(camera, character, diagnostics, wireMaterial, focus, head),
                    DebugView(camera, character, diagnostics, 0f, Color.white, new Color(0.5f, 0.5f, 0.5f), () => Perspective(camera, focus, head)),
                    DebugView(camera, character, diagnostics, 1f, Color.white, new Color(0.5f, 0.5f, 0.5f), () => Perspective(camera, focus, head)),
                };
                Quaternion daylight = sun.transform.rotation;
                for (int i = 0; i < TurntableSteps; i++)
                {
                    sun.transform.rotation = Quaternion.Euler(LightingRig.Day.SunAngles.x, LightingRig.Day.SunAngles.y + 45f * i, 0f);
                    tiles.Add(Snapshot.Shoot(camera, focus + new Vector3(0f, 0.02f, head ? 0.9f : 3.6f), focus, TileWidth, TileHeight));
                }
                sun.transform.rotation = daylight;

                Snapshot.Save(Snapshot.Grid(tiles, Columns, Color.black), $"{Folder}/{part.Id}.png");
                foreach (Texture2D tile in tiles) Object.DestroyImmediate(tile);
                Object.DestroyImmediate(character.gameObject);
            }
            Debug.Log($"[Review] Diagnostic sheets: {parts.Count} parts in {Folder}");
        }

        static bool IsHeadPart(PartEntry part) =>
            part.Slot.StartsWith("hair", StringComparison.Ordinal) || part.Slot == Slots.Hat || part.Slot == Slots.Glasses
            || part.Slot == Slots.Earrings || part.Slot == Slots.HairAccessory || Makeup.IsMark(part.Slot);

        static Texture2D Orthographic(Camera camera, Vector3 focus, float size, Vector3 from, Vector3 up)
        {
            camera.orthographic = true;
            camera.orthographicSize = size;
            camera.transform.SetPositionAndRotation(focus + from * 10f, Quaternion.LookRotation(-from, up));
            camera.farClipPlane = 20f;
            Texture2D image = Snapshot.Shoot(camera, TileWidth, TileHeight);
            camera.orthographic = false;
            return image;
        }

        static Texture2D Perspective(Camera camera, Vector3 focus, bool head)
        {
            float distance = head ? 0.9f : 3.7f;
            return Snapshot.Shoot(camera, focus + new Vector3(0.55f * distance, 0.05f, 0.85f * distance), focus, TileWidth, TileHeight);
        }

        // Twice the magnification of the beauty shot, for small details (graphics plan V1's 2× crops).
        static Texture2D CloseUp(Camera camera, Vector3 focus, bool head)
        {
            float distance = head ? 0.45f : 1.85f;
            return Snapshot.Shoot(camera, focus + new Vector3(0.15f * distance, 0.03f, distance), focus, TileWidth, TileHeight);
        }

        // Every material swapped for a debug view, no post, on a plain background; then put back.
        static Texture2D DebugView(Camera camera, ModularCharacter character, Shader shader, float mode, Color flat, Color background, Func<Texture2D> shoot)
        {
            var swapped = new List<(Renderer Renderer, Material[] Materials)>();
            foreach (Renderer renderer in character.GetComponentsInChildren<Renderer>())
            {
                Material[] original = renderer.sharedMaterials;
                swapped.Add((renderer, original));
                renderer.sharedMaterials = original.Select(m =>
                {
                    var debug = new Material(shader);
                    debug.SetFloat("_Mode", mode);
                    debug.SetColor("_FlatColor", flat);
                    if (m != null && m.HasInteger("_HiddenRegions")) debug.SetInteger("_HiddenRegions", m.GetInteger("_HiddenRegions"));
                    if (m != null && m.HasFloat("_Cull")) debug.SetFloat("_Cull", m.GetFloat("_Cull"));
                    return debug;
                }).ToArray();
            }
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            Color previous = camera.backgroundColor;
            camera.backgroundColor = background;
            data.renderPostProcessing = false;
            Texture2D image = shoot();
            data.renderPostProcessing = true;
            camera.backgroundColor = previous;
            foreach ((Renderer renderer, Material[] materials) in swapped) renderer.sharedMaterials = materials;
            return image;
        }

        // Edges of every visible triangle, drawn over a flat white body.
        static Texture2D Wireframe(Camera camera, ModularCharacter character, Shader shader, Material wire, Vector3 focus, bool head)
        {
            var lines = new List<GameObject>();
            foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var baked = new Mesh();
                renderer.BakeMesh(baked, true);
                int hidden = renderer.sharedMaterial != null && renderer.sharedMaterial.HasInteger("_HiddenRegions") ? renderer.sharedMaterial.GetInteger("_HiddenRegions") : 0;
                lines.Add(Lines(baked, hidden, renderer.transform, wire));
                Object.DestroyImmediate(baked);
            }
            Texture2D image = DebugView(camera, character, shader, 2f, Color.white, Color.white, () => Perspective(camera, focus, head));
            foreach (GameObject line in lines) Object.DestroyImmediate(line);
            return image;
        }

        static GameObject Lines(Mesh mesh, int hiddenRegions, Transform at, Material material)
        {
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            Color[] colours = mesh.colors;
            int[] triangles = mesh.triangles;
            bool Hidden(int v) => colours.Length > 0 && hiddenRegions != 0 && ((hiddenRegions >> Mathf.RoundToInt(colours[v].r * 255f)) & 1) != 0;

            var edges = new HashSet<long>();
            var indices = new List<int>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                if (Hidden(triangles[t]) || Hidden(triangles[t + 1]) || Hidden(triangles[t + 2])) continue;
                for (int e = 0; e < 3; e++)
                {
                    int a = triangles[t + e], b = triangles[t + (e + 1) % 3];
                    long key = Math.Min(a, b) * (long)vertices.Length + Math.Max(a, b);
                    if (!edges.Add(key)) continue;
                    indices.Add(a);
                    indices.Add(b);
                }
            }
            // Lifted a millimetre off the surface so the lines win the depth test.
            var lifted = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) lifted[i] = vertices[i] + (normals.Length > i ? normals[i] * 0.001f : Vector3.zero);
            var lineMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = lifted };
            lineMesh.SetIndices(indices, MeshTopology.Lines, 0);

            var go = new GameObject("Wireframe");
            go.transform.SetPositionAndRotation(at.position, at.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = lineMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static HashSet<string> PartsArgument()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-parts");
            return i >= 0 && i + 1 < args.Length ? new HashSet<string>(args[i + 1].Split(','), StringComparer.Ordinal) : null;
        }
    }
}
