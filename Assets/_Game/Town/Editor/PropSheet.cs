using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.ContentAuthoring;
using Game.Characters.Generation;
using Game.Characters.Review;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Town
{
    /// <summary>
    /// Renders every imported prop under Content/Props (PropModelImporter) as the game draws it, in day light on the
    /// café floor, with a person for scale: front and three-quarter views (Docs/review/props.png).
    /// </summary>
    public static class PropSheet
    {
        const int Width = 1600, Height = 700;
        static readonly Color Floor = new Color(150 / 255f, 108 / 255f, 78 / 255f);

        [MenuItem("Game/Town/Render Prop Sheet")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            List<GameObject> props = AssetDatabase.FindAssets("t:Model", new[] { PropModelImporter.Folder.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .ToList();
            if (props.Count == 0)
            {
                Debug.LogWarning($"[Review] No props under {PropModelImporter.Folder}");
                return;
            }

            // A person first, then the props left to right with a gap between.
            CharacterContent content = StandInContent.Load();
            var person = new CharacterBuilder(content).Build(new CharacterGenerator(content.LoadCatalog()).Generate(3), "casual");
            Poses.Relaxed(person.transform);
            var positions = new List<Vector3> { Vector3.zero };
            float x = 0.5f;
            foreach (GameObject prop in props)
            {
                GameObject made = Object.Instantiate(prop);
                Bounds bounds = Bounds(made);
                x += bounds.extents.x + 0.3f;
                made.transform.position = new Vector3(x - bounds.center.x, 0f, 0f);
                positions.Add(made.transform.position);
                x += bounds.extents.x;
            }
            float centre = x / 2f;
            foreach (Transform root in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.parent == null))
                root.position -= new Vector3(centre, 0f, 0f);

            Material template = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Town/Materials/TownProp.mat");
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.position = new Vector3(0f, -0.05f, -1f);
            floor.transform.localScale = new Vector3(x + 8f, 0.1f, 8f);
            var floorMaterial = new Material(template) { hideFlags = HideFlags.DontSave };
            floorMaterial.SetColor("_BaseColor", Floor);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

            LightingRig.Day.Apply(positions.Select(p => p - new Vector3(centre, 0f, 0f)));
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background);
            float distance = Mathf.Max(4f, x * 1.25f);
            var shots = new List<Texture2D>
            {
                Snapshot.Shoot(camera, new Vector3(0f, 1.2f, distance), new Vector3(0f, 0.75f, 0f), Width, Height),
                Snapshot.Shoot(camera, new Vector3(-distance * 0.75f, 1.7f, distance * 0.95f), new Vector3(0f, 0.8f, 0f), Width, Height),
            };
            string path = GeneratedLineup.Folder + "/props.png";
            Snapshot.Save(Snapshot.StackVertically(shots, Color.black), path);
            Debug.Log($"[Review] Wrote {path}: " + string.Join(", ", props.Select(p => p.name)));
        }

        static Bounds Bounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
