using System.Linq;
using Game.Characters.ContentAuthoring;
using Game.Characters.Review;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Creator
{
    /// <summary>Builds the Creator scene: stage, lighting, camera and the UI document wired to the creator.</summary>
    public static class CreatorSetup
    {
        public const string Folder = "Assets/_Game/Creator";
        public const string ScenePath = Folder + "/Creator.unity";
        public const string PanelSettingsPath = Folder + "/UI/CreatorPanelSettings.asset";
        const string ThemePath = Folder + "/UI/CreatorTheme.tss";
        const string StylePath = Folder + "/UI/Creator.uss";

        [MenuItem("Game/Creator/Build Creator Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PanelSettings panelSettings = PanelSettingsAsset(); // after NewScene, which unloads unused assets

            var stage = new GameObject("Stage").transform;
            LightingRig rig = LightingRig.Day;
            rig.Apply(new[] { Vector3.zero });
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Floor";
            floor.transform.SetParent(stage, false);
            floor.transform.localScale = new Vector3(1.6f, 0.01f, 1.6f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            const string floorPath = Folder + "/UI/CreatorFloor.mat";
            var floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(floorPath);
            if (floorMaterial == null)
            {
                floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "CreatorFloor" };
                floorMaterial.SetColor("_BaseColor", new Color(0.32f, 0.3f, 0.38f));
                AssetDatabase.CreateAsset(floorMaterial, floorPath);
            }
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

            var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.2f, 0.19f, 0.26f);
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.05f;

            var ui = new GameObject("Creator UI");
            var document = ui.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var screen = ui.AddComponent<CreatorScreen>();
            screen.Content = StandInContent.Load();
            screen.Document = document;
            screen.Style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            screen.PreviewCamera = camera;
            screen.Stage = stage;

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (EditorBuildSettings.scenes.All(s => s.path != ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Append(new EditorBuildSettingsScene(ScenePath, true)).ToArray();
            Debug.Log("[Creator] Saved " + ScenePath);
        }

        static PanelSettings PanelSettingsAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (existing != null) return existing;

            AssetDatabase.ImportAsset(ThemePath);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            return settings;
        }
    }
}
