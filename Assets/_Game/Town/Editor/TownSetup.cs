using System.Linq;
using Game.Characters.ContentAuthoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Town
{
    /// <summary>Builds the Town scene: camera, sun, the stage and the HUD document wired to the sandbox.</summary>
    public static class TownSetup
    {
        public const string Folder = "Assets/_Game/Town";
        public const string ScenePath = Folder + "/Town.unity";
        public const string PanelSettingsPath = Folder + "/UI/TownPanelSettings.asset";
        const string ThemePath = "Assets/_Game/Creator/UI/CreatorTheme.tss";
        const string StylePath = Folder + "/UI/Town.uss";
        const string PropMaterialPath = Folder + "/Materials/TownProp.mat";
        const string HighlightMaterialPath = Folder + "/Materials/TownHighlight.mat";
        const string SimCatalogPath = "Assets/_Game/Sim/Content/SimCatalog.json";

        [MenuItem("Game/Town/Build Town Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PanelSettings panelSettings = PanelSettingsAsset(); // after NewScene, which unloads unused assets

            var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            camera.fieldOfView = 32f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 80f;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;

            var stage = new GameObject("Stage").AddComponent<TownStage>();
            stage.Content = StandInContent.Load();
            stage.Camera = camera;
            stage.Sun = sun;
            stage.PropMaterial = MaterialAsset(PropMaterialPath, "Game/Environment/Toon", new Color(0.8f, 0.8f, 0.8f));
            stage.HighlightMaterial = MaterialAsset(HighlightMaterialPath, "Universal Render Pipeline/Unlit", new Color(1f, 0.55f, 0.72f));
            stage.RainMaterial = MaterialAsset(Folder + "/Materials/Rain.mat", "Universal Render Pipeline/Particles/Unlit", new Color(0.78f, 0.84f, 0.95f));

            var ui = new GameObject("Town UI");
            var document = ui.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var screen = ui.AddComponent<TownScreen>();
            screen.Content = stage.Content;
            screen.SimCatalogJson = AssetDatabase.LoadAssetAtPath<TextAsset>(SimCatalogPath);
            screen.Document = document;
            screen.Style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            screen.Stage = stage;

            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterScenes();
            Debug.Log("[Town] Saved " + ScenePath);
        }

        public const string MenuScenePath = Folder + "/MainMenu.unity";
        const string MenuPanelSettingsPath = Folder + "/UI/MenuPanelSettings.asset";

        /// <summary>Builds the main menu scene: a lit stage for the generated cast, a camera, and the menu document.</summary>
        [MenuItem("Game/Town/Build Main Menu Scene")]
        public static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PanelSettings panelSettings = PanelSettingsAsset(MenuPanelSettingsPath);

            var stage = new GameObject("Stage").transform;
            Game.Characters.Review.LightingRig.Day.Apply(new[] { Vector3.zero });
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Floor";
            floor.transform.SetParent(stage, false);
            floor.transform.localScale = new Vector3(6f, 0.01f, 3f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.GetComponent<Renderer>().sharedMaterial = MaterialAsset(Folder + "/Materials/MenuFloor.mat", "Game/Environment/Toon", new Color(0.36f, 0.3f, 0.4f));

            var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.42f, 0.32f, 0.42f);
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.05f;
            // The menu panel sits on the left, so look past the cast: screen-left is world +X for a camera facing -Z.
            camera.transform.position = new Vector3(1.25f, 1.3f, 8.4f);
            camera.transform.LookAt(new Vector3(1.25f, 0.95f, 0f));

            var ui = new GameObject("Menu UI");
            var document = ui.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var menu = ui.AddComponent<MainMenuScreen>();
            menu.Content = StandInContent.Load();
            menu.Document = document;
            menu.Style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            menu.Stage = stage;

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            RegisterScenes();
            Debug.Log("[Town] Saved " + MenuScenePath);
        }

        /// <summary>
        /// Puts the game's scenes first in the build list: the main menu (the build starts there), the creator (where a
        /// new game starts), then the town. Other scenes keep their place after them.
        /// </summary>
        [MenuItem("Game/Town/Register Game Scenes")]
        public static void RegisterScenes()
        {
            string[] game = { MenuScenePath, "Assets/_Game/Creator/Creator.unity", ScenePath };
            var scenes = game.Where(path => AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                .Select(path => new EditorBuildSettingsScene(path, true))
                .Concat(EditorBuildSettings.scenes.Where(s => !game.Contains(s.path)))
                .ToArray();
            EditorBuildSettings.scenes = scenes;
            AssetDatabase.SaveAssets(); // writes ProjectSettings/EditorBuildSettings.asset; batch runs that Exit() skip it otherwise
        }

        static Material MaterialAsset(string path, string shader, Color colour)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(shader)) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            material.SetColor("_BaseColor", colour);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static PanelSettings PanelSettingsAsset(string path = PanelSettingsPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (existing != null) return existing;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            AssetDatabase.CreateAsset(settings, path);
            return settings;
        }
    }
}
