using System.Linq;
using Game.Characters.Expressions;
using Game.Characters.Review;
using Game.Sim;
using Game.Sim.Relationships;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Town
{
    /// <summary>
    /// Plays a scripted visit to the town in Play mode, headless, pressing the HUD's buttons as a player would and
    /// screenshotting along the way (Docs/review/town-*.png): meeting someone at the café, talking, a gift, a date,
    /// the bar in the evening and the park at night. Launch the editor WITHOUT -quit; this exits itself:
    /// Unity.exe -batchmode -projectPath . -executeMethod Game.Town.TownCapture.Run -logFile -
    /// </summary>
    [InitializeOnLoad]
    public static class TownCapture
    {
        const string PendingKey = "Game.Town.TownCapture.Pending";
        const int Width = 1920, Height = 1080;

        // One command every few frames. "press:" presses a HUD button by name ({liked} and {gift} pick something the
        // selected resident loves); "hour:" waits until that hour; "befriend" stands in for a week of visits.
        static readonly string[] Script =
        {
            "shot:arrive",
            "press:here-0", "press:act-greet", "shot:greet",
            "press:act-talk", "press:topic-{liked}", "shot:talk",
            "befriend", "press:act-gift", "press:gift-{gift}", "shot:gift",
            "press:act-date", "press:dateplace-park", "shot:date",
            "press:act-end-date", "press:place-mall", "press:buy-0", "shot:boutique",
            "hour:20", "press:place-bar", "press:here-0", "shot:bar",
            "hour:22", "press:place-park", "shot:park-night",
            "press:place-home", "shot:wardrobe",
        };

        static int _frame, _step;
        static RenderTexture _ui, _scene;

        static TownCapture()
        {
            if (SessionState.GetBool(PendingKey, false)) EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Time.captureFramerate = 30; // fixed steps, so expressions and springs settle the same way every run
            FaceExpression.Blinking = false;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            TownSetup.BuildScene();
            EditorSceneManager.OpenScene(TownSetup.ScenePath);
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(TownSetup.PanelSettingsPath);
            settings.targetTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.DontSave };
            settings.clearColor = true;
            settings.colorClearValue = new Color(0f, 0f, 0f, 0f);
            ShaderUtil.allowAsyncCompilation = false;
            SessionState.SetBool(PendingKey, true);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            _frame++;
            if (_frame > 6000)
            {
                Debug.LogError("[TownCapture] Timed out.");
                Finish(2);
                return;
            }
            if (_frame < 40 || _frame % 12 != 0) return;

            var screen = Object.FindAnyObjectByType<TownScreen>();
            if (screen == null || screen.Session == null) return;
            if (_ui == null)
            {
                _ui = screen.Document.panelSettings.targetTexture;
                _scene = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                screen.Stage.Camera.targetTexture = _scene;
            }
            if (_step == Script.Length)
            {
                Finish(0);
                return;
            }

            string command = Script[_step++];
            int colon = command.IndexOf(':');
            string verb = colon < 0 ? command : command.Substring(0, colon);
            string argument = colon < 0 ? "" : command.Substring(colon + 1);
            switch (verb)
            {
                case "shot":
                    foreach (FaceExpression face in Object.FindObjectsByType<FaceExpression>(FindObjectsSortMode.None)) face.Snap();
                    screen.Stage.SnapCamera();
                    screen.Stage.Camera.Render();
                    Save(argument);
                    break;
                case "press":
                    Press(screen, Resolve(screen, argument));
                    break;
                case "hour":
                    int hour = int.Parse(argument);
                    SimWorld world = screen.Session.World;
                    int minutes = (hour * 60 - (world.Now.Hour * 60 + world.Now.Minute) + 24 * 60) % (24 * 60);
                    screen.Session.Wait(minutes);
                    break;
                case "befriend":
                    Relationship rel = screen.Session.World.RelationshipWith(screen.Session.Selected);
                    rel.Familiarity = 45f;
                    rel.Affection = 58f;
                    rel.Attraction = 60f;
                    rel.Trust = 32f;
                    screen.View.Refresh();
                    break;
            }
        }

        static string Resolve(TownScreen screen, string name)
        {
            Resident selected = screen.Session.Selected;
            if (selected == null) return name;
            SimCatalog catalog = screen.Session.World.Catalog;
            string liked = selected.Profile.Interests.Where(i => i.Value >= 0.5f).OrderByDescending(i => i.Value).Select(i => i.Key).First();
            string gift = catalog.Gifts.First(g => g.Topics.Contains(liked)).Id;
            return name.Replace("{liked}", liked).Replace("{gift}", gift);
        }

        static void Press(TownScreen screen, string name)
        {
            Button button = screen.Document.rootVisualElement.Q<Button>(name);
            if (button == null)
            {
                Debug.LogError($"[TownCapture] No button '{name}'.");
                Finish(1);
                return;
            }
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
            Debug.Log($"[TownCapture] Pressed {name}: {screen.Session.Notice ?? screen.Session.Last?.Line ?? ""}");
        }

        static void Save(string shot)
        {
            Texture2D scene = Read(_scene);
            Texture2D ui = Read(_ui);
            Color[] background = scene.GetPixels(), overlay = ui.GetPixels();
            for (int i = 0; i < background.Length; i++)
                background[i] = Color.Lerp(background[i], new Color(overlay[i].r, overlay[i].g, overlay[i].b), overlay[i].a);
            scene.SetPixels(background);
            scene.Apply();
            Snapshot.Save(scene, $"Docs/review/town-{shot}.png");
            Object.DestroyImmediate(scene);
            Object.DestroyImmediate(ui);
            Debug.Log($"[TownCapture] Saved town-{shot}.png");
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
            Time.captureFramerate = 0;
            FaceExpression.Blinking = true;
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(TownSetup.PanelSettingsPath);
            if (settings != null) settings.targetTexture = null;
            EditorApplication.Exit(code);
        }
    }
}
