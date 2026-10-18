using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Characters.Review
{
    /// <summary>
    /// The lighting sheet (graphics plan G0.10, Docs/review/lighting.png): the same four characters, lightest to darkest
    /// skin, on a toon-shaded patch of ground with a tree and a lamp post, under every lighting preset the town uses.
    /// Rows, top to bottom: day, sunset, night, rain, interior. Sky, fog, glow and grading as in the game; no vignette.
    /// The main A/B target for character shading work.
    /// Headless: -executeMethod Game.Characters.Review.LightingSheet.Render
    /// </summary>
    public static class LightingSheet
    {
        const int Width = 1200, Height = 450;
        static readonly float[] Tones = { 0.04f, 0.36f, 0.66f, 0.96f };
        static readonly Color Ground = new Color(0.64f, 0.62f, 0.58f), Wall = new Color(232 / 255f, 210 / 255f, 180 / 255f);

        [MenuItem("Game/Characters/Review/Render Lighting Sheet")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = StandInContent.Load();
            ContentCatalog catalog = content.LoadCatalog();
            var builder = new CharacterBuilder(content);
            var generator = new CharacterGenerator(catalog);

            // Four people across the skin ramp: the first seeds nearest each target tone.
            List<CharacterDna> candidates = Enumerable.Range(1, 80).Select(seed => generator.Generate((ulong)seed)).ToList();
            var cast = new List<CharacterDna>();
            foreach (float tone in Tones)
                cast.Add(candidates.Where(d => !cast.Contains(d)).OrderBy(d => Mathf.Abs(d.Skin.Tone - tone)).First());
            for (int i = 0; i < cast.Count; i++)
            {
                ModularCharacter character = builder.Build(cast[i], "casual");
                character.transform.position = new Vector3(-1.65f + 1.1f * i, 0f, 0f);
                Poses.Relaxed(character.transform);
            }

            Material prop = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Town/Materials/TownProp.mat");
            Renderer floor = Block(PrimitiveType.Cube, new Vector3(0f, -0.05f, -1f), new Vector3(14f, 0.1f, 10f), prop, Ground);
            var outdoorProps = new List<Renderer>
            {
                Block(PrimitiveType.Cylinder, new Vector3(-3.4f, 1.1f, -3.2f), new Vector3(0.3f, 1.1f, 0.3f), prop, new Color(0.43f, 0.31f, 0.24f)),
                Block(PrimitiveType.Sphere, new Vector3(-3.4f, 2.8f, -3.2f), new Vector3(2.1f, 1.9f, 2.1f), prop, new Color(0.31f, 0.55f, 0.32f)),
                Block(PrimitiveType.Cylinder, new Vector3(3.1f, 1.4f, -1.6f), new Vector3(0.08f, 1.4f, 0.08f), prop, new Color(0.21f, 0.23f, 0.26f)),
                Block(PrimitiveType.Sphere, new Vector3(3.1f, 2.9f, -1.6f), Vector3.one * 0.34f, prop, new Color(0.98f, 0.91f, 0.77f)),
            };
            Material bulb = outdoorProps[3].sharedMaterial;
            Renderer wall = Block(PrimitiveType.Cube, new Vector3(0f, 2.5f, -3.4f), new Vector3(14f, 5f, 0.2f), prop, Wall);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            var lamps = new List<Light>();
            foreach (float x in new[] { 1.2f, -1.6f })
            {
                var lamp = new GameObject("Lamp").AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.range = 5f;
                lamp.transform.position = new Vector3(x, 2.6f, 1.6f);
                lamps.Add(lamp);
            }

            Camera camera = Snapshot.CreateCamera(Color.black);
            camera.GetUniversalAdditionalCameraData().volumeLayerMask = 1; // the scene's grading, which tiles otherwise skip
            var lighting = new SceneLighting(sun, camera);
            LightingLibrary library = LightingPresets.Library();
            var rows = new (LightingLook Look, bool Indoors, bool Wet)[]
            {
                (library.Outdoors(12 * 60), false, false),
                (library.Outdoors(18 * 60 + 30), false, false),
                (library.Outdoors(22 * 60), false, false),
                (library.Outdoors(12 * 60).Overcast(0.65f, 0.35f), false, true),
                (library.Get(LightingLibrary.Interior).Look.Walls(Wall), true, false),
            };

            var shots = new List<Texture2D>();
            foreach ((LightingLook look, bool indoors, bool wet) in rows)
            {
                wall.gameObject.SetActive(indoors);
                foreach (Renderer outdoor in outdoorProps) outdoor.gameObject.SetActive(!indoors);
                floor.sharedMaterial.SetColor("_BaseColor", wet ? Ground * 0.78f : Ground);
                bulb.SetColor("_EmissionColor", look.LampColour * look.Glow);
                for (int i = 0; i < lamps.Count; i++)
                {
                    lamps[i].gameObject.SetActive(look.Lamps);
                    lamps[i].color = look.LampColour;
                    lamps[i].intensity = look.LampIntensity;
                    lamps[i].shadows = i == 0 && !indoors && look.Lamps ? LightShadows.Soft : LightShadows.None;
                }
                lighting.Apply(look, indoors ? Wall : (Color?)null, fog: true, vignette: false);
                shots.Add(Snapshot.Shoot(camera, new Vector3(0f, 1.55f, 7.6f), new Vector3(0f, 1.05f, -0.4f), Width, Height));
            }
            Snapshot.Save(Snapshot.StackVertically(shots, Color.black), GeneratedLineup.Folder + "/lighting.png");
            Debug.Log("[Review] Lighting sheet: day, sunset, night, rain, interior; skin tones " +
                      string.Join(", ", cast.Select(d => d.Skin.Tone.ToString("0.00"))));
        }

        // A primitive with outline normals and its own copy of the material in the given colour.
        static Renderer Block(PrimitiveType shape, Vector3 position, Vector3 scale, Material material, Color colour)
        {
            GameObject made = GameObject.CreatePrimitive(shape);
            Object.DestroyImmediate(made.GetComponent<Collider>());
            made.transform.position = position;
            made.transform.localScale = scale;
            MeshFilter filter = made.GetComponent<MeshFilter>();
            Mesh mesh = Object.Instantiate(filter.sharedMesh);
            OutlineNormals.Bake(mesh);
            filter.sharedMesh = mesh;
            var renderer = made.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(material) { hideFlags = HideFlags.DontSave };
            renderer.sharedMaterial.SetColor("_BaseColor", colour);
            return renderer;
        }
    }
}
