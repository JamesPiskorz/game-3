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
            stage.PropMaterial = MaterialAsset(PropMaterialPath, "Universal Render Pipeline/Lit", new Color(0.8f, 0.8f, 0.8f));
            stage.HighlightMaterial = MaterialAsset(HighlightMaterialPath, "Universal Render Pipeline/Unlit", new Color(1f, 0.55f, 0.72f));

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

        /// <summary>
        /// Puts the game's scenes first in the build list: the creator (where a new game starts), then the town the
        /// creator's "Into town" button loads. Other scenes keep their place after them.
        /// </summary>
        [MenuItem("Game/Town/Register Game Scenes")]
        public static void RegisterScenes()
        {
            string[] game = { "Assets/_Game/Creator/Creator.unity", ScenePath };
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

        static PanelSettings PanelSettingsAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (existing != null) return existing;

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
