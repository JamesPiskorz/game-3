using System.IO;
using Game.Characters.Review;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Creator
{
    /// <summary>
    /// Screenshots the creator in Play mode, headless: the UI panel and the preview camera render into textures that
    /// are composited into one PNG per tab (Docs/review/creator-*.png). Launch the editor WITHOUT -quit; this exits
    /// itself when done:
    /// Unity.exe -batchmode -projectPath . -executeMethod Game.Creator.CreatorCapture.Run -logFile -
    /// </summary>
    [InitializeOnLoad]
    public static class CreatorCapture
    {
        const string PendingKey = "Game.Creator.CreatorCapture.Pending";
        const int Width = 1920, Height = 1080;
        // Each step presses a button by name, as a player would, then screenshots the result.
        static readonly (string Button, string Shot)[] Steps =
        {
            ("tab-body", "body"), ("tab-face", "face"), ("tab-skin", "skin"), ("tab-hair", "hair"), ("tab-outfit", "outfit"),
            ("randomize", "randomized"), ("context-date", "date"),
        };

        static int _frame;
        static int _tab;
        static RenderTexture _ui, _scene;

        // Entering Play mode may or may not reload the domain (Enter Play Mode Options): hook up both ways.
        static CreatorCapture()
        {
            if (SessionState.GetBool(PendingKey, false)) EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            CreatorSetup.BuildScene();
            EditorSceneManager.OpenScene(CreatorSetup.ScenePath);
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(CreatorSetup.PanelSettingsPath);
            settings.targetTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.DontSave };
            settings.clearColor = true;
            settings.colorClearValue = new Color(0f, 0f, 0f, 0f);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            _frame++;
            if (_frame > 3000)
            {
                Debug.LogError("[CreatorCapture] Timed out.");
                Finish(2);
                return;
            }
            if (_frame < 30 || _frame % 15 != 0) return; // let the UI lay out and the camera settle between shots

            var screen = Object.FindAnyObjectByType<CreatorScreen>();
            if (screen == null || screen.Session == null) return;
            PanelSettings settings = screen.Document.panelSettings;
            if (_ui == null)
            {
                _ui = settings.targetTexture;
                _scene = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                screen.PreviewCamera.targetTexture = _scene;
            }

            if (_tab > 0)
                Save(screen, Steps[_tab - 1].Shot);
            if (_tab == Steps.Length)
            {
                Finish(0);
                return;
            }

            Button button = screen.Document.rootVisualElement.Q<Button>(Steps[_tab].Button);
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
            screen.SnapCamera();
            _tab++;
        }

        static void Save(CreatorScreen screen, string tab)
        {
            Texture2D scene = Read(_scene);
            Texture2D ui = Read(_ui);
            Color[] background = scene.GetPixels(), overlay = ui.GetPixels();
            for (int i = 0; i < background.Length; i++)
                background[i] = Color.Lerp(background[i], new Color(overlay[i].r, overlay[i].g, overlay[i].b), overlay[i].a);
            scene.SetPixels(background);
            scene.Apply();
            Snapshot.Save(scene, $"Docs/review/creator-{tab}.png");
            Object.DestroyImmediate(scene);
            Object.DestroyImmediate(ui);
            Debug.Log($"[CreatorCapture] Saved creator-{tab}.png");
        }

        static Texture2D Read(RenderTexture source)
        {
            RenderTexture resolved = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, resolved);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = resolved;
            var image = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(resolved);
            return image;
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(PendingKey, false);
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(CreatorSetup.PanelSettingsPath);
            if (settings != null) settings.targetTexture = null;
            EditorApplication.Exit(code);
        }
    }
}
