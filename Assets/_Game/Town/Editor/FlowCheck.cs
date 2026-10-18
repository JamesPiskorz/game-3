using System.IO;
using Game.Characters.Building;
using Game.Characters.Dna;
using Game.Creator;
using Game.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Town
{
    /// <summary>
    /// End-to-end check of the whole game loop, headless, pressing the real buttons. It goes:
    /// 1. main menu, then New game;
    /// 2. the creator: type a name, Randomize, Into town (the town's player must be exactly the creator's
    ///    character, called by that name);
    /// 3. Menu, which autosaves;
    /// 4. Continue (the town must come back at the saved time, with the same player).
    /// It screenshots the menu to Docs/review/menu.png. The player's saved look and save file are backed up and
    /// restored, so running it leaves a real player's game alone. Exits 0 on success. Launch WITHOUT -quit:
    /// Unity.exe -batchmode -projectPath . -executeMethod Game.Town.FlowCheck.Run -logFile -
    /// </summary>
    [InitializeOnLoad]
    public static class FlowCheck
    {
        const string PendingKey = "Game.Town.FlowCheck.Pending";
        const string LookBackupKey = "Game.Town.FlowCheck.Look";
        const string SaveBackupKey = "Game.Town.FlowCheck.Save";
        const string NameBackupKey = "Game.Town.FlowCheck.Name";
        const int Width = 1920, Height = 1080;

        static int _frame, _step, _waitFrom;
        static bool _photoShot;
        static string _expectedLook;
        static long _savedMinutes;
        static RenderTexture _ui, _scene;

        static FlowCheck()
        {
            if (SessionState.GetBool(PendingKey, false)) EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static void Run()
        {
            TownSetup.BuildScene();
            TownSetup.BuildMenuScene();
            SessionState.SetString(LookBackupKey, PlayerPrefs.GetString(PlayerLook.Key, ""));
            SessionState.SetString(NameBackupKey, PlayerPrefs.GetString(PlayerLook.NameKey, ""));
            SessionState.SetString(SaveBackupKey, File.Exists(TownScreen.SavePath) ? File.ReadAllText(TownScreen.SavePath) : "");
            PlayerPrefs.DeleteKey(PlayerLook.Key);
            if (File.Exists(TownScreen.SavePath)) File.Delete(TownScreen.SavePath);

            EditorSceneManager.OpenScene(TownSetup.MenuScenePath);
            var menu = Object.FindAnyObjectByType<MainMenuScreen>();
            menu.CastSeed = 2026; // a fixed cast for the screenshot; not saved to the scene
            _ui = PanelShot.Target(Width, Height);
            menu.Document.panelSettings.targetTexture = _ui;
            menu.Document.panelSettings.clearColor = true;
            menu.Document.panelSettings.colorClearValue = new Color(0f, 0f, 0f, 0f);

            ShaderUtil.allowAsyncCompilation = false;
            SessionState.SetBool(PendingKey, true);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Time.captureFramerate = 30;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            _frame++;
            if (_frame > 6000)
            {
                Fail($"Timed out at step {_step}.");
                return;
            }
            if (_frame - _waitFrom < 30) return; // let each scene start and lay out

            switch (_step)
            {
                case 0: // the menu: screenshot it, then start a new game
                {
                    var menu = Object.FindAnyObjectByType<MainMenuScreen>();
                    if (menu == null) return;
                    if (_scene == null)
                    {
                        _scene = PanelShot.Target(Width, Height, Game.Characters.Rendering.GameCamera.MsaaSamples); // as the screen renders
                        Camera.main.targetTexture = _scene;
                        return;
                    }
                    Camera.main.Render();
                    if (menu.Swaps == 0)
                    {
                        PanelShot.Save(_scene, _ui, "Docs/review/menu.png");
                        menu.Swap(); // and once someone new has stepped in
                        _waitFrom = _frame;
                        return;
                    }
                    PanelShot.Save(_scene, _ui, "Docs/review/menu-new-face.png");
                    Camera.main.targetTexture = null;
                    menu.Document.panelSettings.targetTexture = null;
                    Next(Press(menu.Document, "new-game"));
                    break;
                }
                case 1: // the creator: randomize and head into town
                {
                    var creator = Object.FindAnyObjectByType<CreatorScreen>();
                    if (creator == null || creator.Session == null) return;
                    var document = creator.GetComponent<UIDocument>();
                    TextField name = document.rootVisualElement.Q<TextField>("player-name");
                    if (name == null)
                    {
                        Fail("The creator has no name field.");
                        return;
                    }
                    name.value = "Robin";
                    if (!Press(document, "randomize")) return;
                    _expectedLook = creator.Session.ShareCode;
                    Next(Press(document, "play"));
                    break;
                }
                case 2: // the town: same character; then back to the menu, which saves
                {
                    var town = Object.FindAnyObjectByType<TownScreen>();
                    if (town == null || town.Session == null) return;
                    string look = DnaSerializer.ToShareCode(town.Session.World.Player.Dna);
                    if (look != _expectedLook)
                    {
                        Fail("The town's player isn't the creator's character.");
                        return;
                    }
                    if (town.Session.World.Player.Name != "Robin")
                    {
                        Fail($"The town calls the player '{town.Session.World.Player.Name}', not the name typed in the creator.");
                        return;
                    }
                    town.Session.Wait(90); // so a continue at the start time can't pass by accident
                    // A keepsake to come back to: the menu should show it under Continue.
                    Resident with = town.Session.World.Residents[0];
                    town.Session.World.Photos.Add(new Game.Sim.Relationships.Photo
                    {
                        Resident = 0, Day = town.Session.World.Now.Day, Location = "park", Name = with.Profile.FirstName, Place = "Willow Park",
                        PlayerLook = DnaSerializer.ToJson(town.Session.World.Player.Dna, indented: false), TheirLook = DnaSerializer.ToJson(with.Dna, indented: false),
                    });
                    _savedMinutes = town.Session.World.Now.Minutes;
                    Debug.Log($"[FlowCheck] Town opened with the creator's character; leaving at {town.Session.World.Now}.");
                    Next(Press(town.Document, "menu"));
                    break;
                }
                case 3: // the menu again: Continue must be available now
                {
                    var menu = Object.FindAnyObjectByType<MainMenuScreen>();
                    if (menu == null) return;
                    Button resume = menu.Document.rootVisualElement.Q<Button>("continue");
                    if (resume == null || !resume.enabledSelf)
                    {
                        Fail("Continue isn't available after leaving town.");
                        return;
                    }
                    if (menu.Document.rootVisualElement.Q("last-photo") == null)
                    {
                        Fail("The menu doesn't show the saved game's latest photo.");
                        return;
                    }
                    // Screenshot it once, after a frame's layout with the targets back on.
                    if (!_photoShot)
                    {
                        if (menu.Document.panelSettings.targetTexture == null)
                        {
                            menu.Document.panelSettings.targetTexture = _ui;
                            Camera.main.targetTexture = _scene;
                            _waitFrom = _frame;
                            return;
                        }
                        Camera.main.Render();
                        PanelShot.Save(_scene, _ui, "Docs/review/menu-photo.png");
                        Camera.main.targetTexture = null;
                        menu.Document.panelSettings.targetTexture = null;
                        _photoShot = true;
                    }
                    Next(Press(menu.Document, "continue"));
                    break;
                }
                case 4: // continued: the same time and the same player
                {
                    var town = Object.FindAnyObjectByType<TownScreen>();
                    if (town == null || town.Session == null) return;
                    SimWorld world = town.Session.World;
                    if (world.Now.Minutes != _savedMinutes || DnaSerializer.ToShareCode(world.Player.Dna) != _expectedLook)
                    {
                        Fail($"Continue didn't restore the game: it's {world.Now}, expected minute {_savedMinutes}.");
                        return;
                    }
                    Debug.Log($"[FlowCheck] OK: menu, creator, town, menu and continue all work; resumed at {world.Now}.");
                    Finish(0);
                    break;
                }
            }
        }

        static void Next(bool pressed)
        {
            if (!pressed) return;
            _step++;
            _waitFrom = _frame;
        }

        static bool Press(UIDocument document, string name)
        {
            Button button = document.rootVisualElement.Q<Button>(name);
            if (button == null) return false;
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
            return true;
        }

        static void Fail(string message)
        {
            Debug.LogError("[FlowCheck] " + message);
            Finish(1);
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(PendingKey, false);
            Time.captureFramerate = 0;

            string look = SessionState.GetString(LookBackupKey, "");
            if (look.Length > 0) PlayerPrefs.SetString(PlayerLook.Key, look);
            else PlayerPrefs.DeleteKey(PlayerLook.Key);
            string name = SessionState.GetString(NameBackupKey, "");
            if (name.Length > 0) PlayerPrefs.SetString(PlayerLook.NameKey, name);
            else PlayerPrefs.DeleteKey(PlayerLook.NameKey);
            PlayerPrefs.Save();
            string save = SessionState.GetString(SaveBackupKey, "");
            if (save.Length > 0) File.WriteAllText(TownScreen.SavePath, save);
            else if (File.Exists(TownScreen.SavePath)) File.Delete(TownScreen.SavePath);
            EditorApplication.Exit(code);
        }
    }
}
