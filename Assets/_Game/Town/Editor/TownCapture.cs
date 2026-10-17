using System.Linq;
using Game.Characters.Expressions;
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
        // selected resident loves, {best} and {chat} the reply that suits them); "hour:" waits until that hour; "befriend" stands in for a week of visits
        // ("smitten" for a few more);
        // "pause:" lets frames pass; "shot:" snaps everyone into place first, "live:" shoots them mid-motion.
        static readonly string[] Script =
        {
            "shot:arrive",
            "press:here-0", "press:act-greet", "shot:greet",
            "press:act-talk", "press:topic-{liked}", "shot:talk",
            "befriend", "press:act-gift", "press:gift-{gift}", "shot:gift",
            "press:act-chat", "shot:chat", "press:chatopt-{chat}", "shot:chat-reply", "press:chatopt-{chat}", "shot:chat-heard",
            "press:act-date", "press:dateplace-park", "shot:date",
            "press:dateopt-{best}", "shot:date-round", "press:dateopt-{best}", "press:dateopt-{best}", "shot:date-end",
            "press:act-end-date", "press:place-mall", "press:buy-0", "shot:boutique", "press:cut-{cut}", "shot:salon", "press:lipstick-1", "press:blush-toggle", "shot:salon-makeup",
            "hour:20", "press:place-bar", "press:here-0", "shot:bar", "press:act-gift", "shot:give-picker", "press:giveitem-0", "shot:give-clothes",
            "smitten", "press:act-greet", "shot:confess", "press:confess-yes", "shot:confess-yes",
            "hour:22", "press:place-park", "shot:park-night",
            "press:wait", "pause:75", "live:park-comings-and-goings",
            "press:place-home", "shot:wardrobe",
            "press:contacts", "shot:contacts",
            "close", "sleep-until-invited", "press:messages", "shot:messages",
            "press:messages", "rainy-day", "press:place-park", "shot:park-rain",
            "press:place-library", "press:activity-read", "shot:library-read",
            "busy:gym", "press:place-gym", "shot:gym",
        };

        static int _frame, _step, _pause;
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
            TownScreen.SavingEnabled = false; // the visit sleeps through days; never touch the real save
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

            if (_pause > 0)
            {
                _pause -= 12;
                return;
            }
            string command = Script[_step++];
            int colon = command.IndexOf(':');
            string verb = colon < 0 ? command : command.Substring(0, colon);
            string argument = colon < 0 ? "" : command.Substring(colon + 1);
            switch (verb)
            {
                case "shot":
                    foreach (FaceExpression face in Object.FindObjectsByType<FaceExpression>()) face.Snap();
                    screen.Stage.SnapCamera();
                    screen.Stage.Camera.Render();
                    Save(argument);
                    break;
                case "pause": // let things play out for a number of frames
                    _pause = int.Parse(argument);
                    break;
                case "live": // as things are, mid-motion: frame the camera but don't snap anyone into place
                    screen.Stage.SnapCamera(characters: false);
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
                case "busy": // wait, an hour at a time, until at least two people are at a place (at most a day)
                {
                    SimWorld w = screen.Session.World;
                    for (int waited = 0; waited < 24 && (w.At(argument).Count() < 2 || !screen.Session.IsOpen(argument)); waited++)
                        screen.Session.Wait(60);
                    break;
                }
                case "rainy-day": // the next rainy morning, at home
                {
                    SimWorld w = screen.Session.World;
                    int day = w.Now.Day + 1;
                    while (Forecast.For(w.Seed, day) != Weather.Rainy) day++;
                    while (w.Now.Day < day) screen.Session.Sleep();
                    screen.Session.Wait(150); // late morning
                    break;
                }
                case "sleep-until-invited":
                    for (int night = 0; night < 10 && screen.Session.World.Invitations.Count == 0; night++) screen.Session.Sleep();
                    break;
                case "close": // make the player close to everyone they've met, so invitations come
                    foreach (Game.Sim.Relationships.Relationship known in screen.Session.World.Relationships)
                    {
                        if (!known.Met) continue;
                        known.Familiarity = Mathf.Max(known.Familiarity, 65f);
                        known.Affection = Mathf.Max(known.Affection, 75f);
                        known.Trust = Mathf.Max(known.Trust, 40f);
                    }
                    break;
                case "smitten": // the selected resident has fallen for the player since yesterday, so the next hello is a confession
                {
                    Relationship smitten = screen.Session.World.RelationshipWith(screen.Session.Selected);
                    smitten.Familiarity = Mathf.Max(smitten.Familiarity, 65f);
                    smitten.Affection = 92f;
                    smitten.Attraction = 90f;
                    smitten.Trust = Mathf.Max(smitten.Trust, 40f);
                    smitten.LastSeenDay = screen.Session.World.Now.Day - 1;
                    break;
                }
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
            // A cut the player doesn't have yet, for the salon.
            string cut = Game.Sim.Sandbox.Salon.Cuts(screen.Session.World.Characters)
                .First(c => !screen.Session.World.Player.Dna.Hair.PieceIds.Contains(c.Id) && !c.RequiresTags.Any()).Id;
            name = name.Replace("{cut}", cut);
            Resident selected = screen.Session.Selected;
            if (selected == null) return name;
            SimCatalog catalog = screen.Session.World.Catalog;
            string liked = selected.Profile.Interests.Where(i => i.Value >= 0.5f).OrderByDescending(i => i.Value).Select(i => i.Key).First();
            string gift = catalog.Gifts.First(g => g.Topics.Contains(liked)).Id;
            string best = screen.Session.Date == null || screen.Session.Date.Finished ? "0" : screen.Session.Date.BestOption().ToString();
            ChatScene chat = screen.Session.Chat;
            string reply = chat == null ? "0" : chat.Replies.IndexOf(chat.Replies.OrderByDescending(r => ChatTaste.Preference(selected.Profile.Personality, r.Style, chat.Opener)).First()).ToString();
            return name.Replace("{liked}", liked).Replace("{gift}", gift).Replace("{best}", best).Replace("{chat}", reply);
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
            PanelShot.Save(_scene, _ui, $"Docs/review/town-{shot}.png");
            Debug.Log($"[TownCapture] Saved town-{shot}.png");
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(PendingKey, false);
            Time.captureFramerate = 0;
            FaceExpression.Blinking = true;
            TownScreen.SavingEnabled = true;
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(TownSetup.PanelSettingsPath);
            if (settings != null) settings.targetTexture = null;
            EditorApplication.Exit(code);
        }
    }
}
