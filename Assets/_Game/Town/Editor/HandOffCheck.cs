using Game.Characters.Building;
using Game.Creator;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Town
{
    /// <summary>
    /// End-to-end check of the new-game flow, headless: start in the creator, press Randomize then "Into town", and
    /// confirm the town scene loads with the player wearing exactly the creator's character. The saved look is
    /// backed up and restored, so running this doesn't change what a real player made. Exits 0 on success. Launch
    /// WITHOUT -quit: Unity.exe -batchmode -projectPath . -executeMethod Game.Town.HandOffCheck.Run -logFile -
    /// </summary>
    [InitializeOnLoad]
    public static class HandOffCheck
    {
        const string PendingKey = "Game.Town.HandOffCheck.Pending";
        const string BackupKey = "Game.Town.HandOffCheck.Backup";

        static int _frame;
        static string _expected;

        static HandOffCheck()
        {
            if (SessionState.GetBool(PendingKey, false)) EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static void Run()
        {
            TownSetup.RegisterScenes();
            SessionState.SetString(BackupKey, PlayerPrefs.GetString(PlayerLook.Key, ""));
            PlayerPrefs.DeleteKey(PlayerLook.Key);
            EditorSceneManager.OpenScene("Assets/_Game/Creator/Creator.unity");
            ShaderUtil.allowAsyncCompilation = false;
            SessionState.SetBool(PendingKey, true);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            _frame++;
            if (_frame > 3000)
            {
                Fail("Timed out waiting for the town.");
                return;
            }

            var creator = Object.FindAnyObjectByType<CreatorScreen>();
            if (_expected == null)
            {
                if (_frame < 30 || creator == null || creator.Session == null) return;
                Press(creator.GetComponent<UIDocument>(), "randomize");
                _expected = creator.Session.ShareCode;
                Press(creator.GetComponent<UIDocument>(), "play");
                Debug.Log("[HandOffCheck] Pressed Into town with " + _expected);
                return;
            }

            var town = Object.FindAnyObjectByType<TownScreen>();
            if (town == null || town.Session == null) return;
            string actual = Game.Characters.Dna.DnaSerializer.ToShareCode(town.Session.World.Player.Dna);
            if (actual != _expected)
            {
                Fail($"The town's player isn't the creator's character.\n  creator: {_expected}\n  town:    {actual}");
                return;
            }
            Debug.Log($"[HandOffCheck] OK: the town opened at {town.Session.LocationName(town.Session.Location)} with the creator's character.");
            Finish(0);
        }

        static void Press(UIDocument document, string name)
        {
            Button button = document.rootVisualElement.Q<Button>(name);
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        static void Fail(string message)
        {
            Debug.LogError("[HandOffCheck] " + message);
            Finish(1);
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(PendingKey, false);
            string backup = SessionState.GetString(BackupKey, "");
            if (backup.Length > 0) PlayerPrefs.SetString(PlayerLook.Key, backup);
            else PlayerPrefs.DeleteKey(PlayerLook.Key);
            PlayerPrefs.Save();
            EditorApplication.Exit(code);
        }
    }
}
