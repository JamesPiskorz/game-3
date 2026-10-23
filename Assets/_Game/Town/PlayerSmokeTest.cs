using System.Collections;
using System.Linq;
using Game.Sim;
using Game.Sim.Sandbox;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Town
{
    /// <summary>
    /// A smoke test for real player builds. Launched with <c>-smoke</c>, the game goes from the main menu into town,
    /// plays a short day through the session (meet people, talk, work, shop, sleep) and quits with exit code 0, or 1
    /// on any error or exception. That proves the build runs outside the editor:
    /// <c>Game3.exe -batchmode -smoke -logFile smoke.log</c>
    /// Without the flag it does nothing.
    /// </summary>
    public static class PlayerSmokeTest
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (!System.Environment.GetCommandLineArgs().Contains("-smoke")) return;
            TownScreen.SavingEnabled = false; // never overwrite a real player's game
            var runner = new GameObject("Smoke test").AddComponent<Runner>();
            Object.DontDestroyOnLoad(runner.gameObject);
        }

        sealed class Runner : MonoBehaviour
        {
            int _errors;
            string _firstError;

            void OnEnable() => Application.logMessageReceived += Watch;
            void OnDisable() => Application.logMessageReceived -= Watch;

            void Watch(string message, string stack, LogType type)
            {
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                if (message.StartsWith("[Smoke]")) return;
                _errors++;
                _firstError ??= message;
            }

            IEnumerator Start()
            {
                Debug.Log("[Smoke] Started in scene " + SceneManager.GetActiveScene().name);
                yield return null;
                if (FindAnyObjectByType<MainMenuScreen>() == null)
                {
                    Finish(false, "The first scene isn't the main menu.");
                    yield break;
                }

                SceneManager.LoadScene(MainMenuScreen.TownScene);
                TownScreen town = null;
                for (int frame = 0; frame < 600 && (town == null || town.Session == null); frame++)
                {
                    yield return null;
                    town = FindAnyObjectByType<TownScreen>();
                }
                if (town == null || town.Session == null)
                {
                    Finish(false, "The town never started.");
                    yield break;
                }

                TownSession session = town.Session;
                string busiest = session.World.Catalog.Locations.Where(l => session.IsOpen(l.Id))
                    .OrderByDescending(l => session.World.At(l.Id).Count()).First().Id;
                session.Travel(busiest);
                yield return null;
                int met = 0;
                foreach (Resident resident in session.Present().Take(3))
                {
                    session.Select(resident);
                    if (session.Greet()?.FirstMeeting == true) met++;
                    session.Talk(session.World.Catalog.Topics[0].Id);
                    yield return null;
                }
                int built = town.Stage.GetComponentsInChildren<Game.Characters.Modular.ModularCharacter>().Length;
                if (session.CanWork) session.Work();
                session.Travel(Boutique.Location);
                ShopItem item = session.Shop().FirstOrDefault(s => s.Price <= session.World.Player.Money && !session.Owns(s.Item));
                if (item != null) session.Buy(item);
                yield return null;
                session.Sleep();
                for (int i = 0; i < 30; i++) yield return null; // let a few frames render and walk

                if (met == 0 || built == 0)
                {
                    Finish(false, $"Nothing happened in town (met {met}, characters on stage {built}).");
                    yield break;
                }
                if (System.Environment.GetCommandLineArgs().Contains("-smoke-feedback"))
                {
                    string folder = null;
                    yield return Game.Feedback.FeedbackReporter.Instance.SaveTicket("Smoke test", "Saved by the player smoke test.", saved => folder = saved);
                    string problem = CheckTicket(folder);
                    if (problem != null)
                    {
                        Finish(false, "Feedback ticket: " + problem);
                        yield break;
                    }
                    Debug.Log("[Smoke] Feedback ticket saved: " + folder);

                    // For review: the form as the player sees it (-smoke-feedback-form <png>).
                    string formShot = Game.Feedback.FeedbackPaths.Argument("-smoke-feedback-form");
                    if (formShot != null && !Application.isBatchMode)
                    {
                        Game.Feedback.FeedbackReporter.Instance.Open();
                        for (int frame = 0; frame < 120 && !Game.Feedback.FeedbackReporter.Instance.IsOpen; frame++) yield return null;
                        for (int frame = 0; frame < 5; frame++) yield return null;
                        yield return new WaitForEndOfFrame();
                        var shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                        shot.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0, false);
                        System.IO.File.WriteAllBytes(formShot, shot.EncodeToPNG());
                        Destroy(shot);
                        Game.Feedback.FeedbackReporter.Instance.Close();
                    }
                }
                Finish(_errors == 0, _errors == 0
                    ? $"Played a day: met {met} people, {built} characters on stage, now {session.World.Now}."
                    : $"{_errors} errors, first: {_firstError}");
            }

            /// <summary>What's missing from a ticket saved in town, or null: its text, the state with people on screen, the town, the save, the screenshot.</summary>
            static string CheckTicket(string folder)
            {
                if (folder == null || !System.IO.File.Exists(System.IO.Path.Combine(folder, Game.Feedback.FeedbackTicket.MarkdownFile))) return "no ticket.md";
                var context = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, Game.Feedback.FeedbackTicket.ContextFile)));
                if (!(context["characters"] is Newtonsoft.Json.Linq.JArray characters) || !characters.Any(c => (bool)c["onScreen"] && c["dna"]?.Type == Newtonsoft.Json.Linq.JTokenType.Object))
                    return "nobody on screen with their DNA";
                if (context["game"]?["town"]?["now"] == null) return "no town state";
                if (!System.IO.File.Exists(System.IO.Path.Combine(folder, "town-save.json"))) return "no town save";
                if (!Application.isBatchMode && !System.IO.File.Exists(System.IO.Path.Combine(folder, Game.Feedback.FeedbackTicket.ScreenshotFile))) return "no screenshot";
                return null;
            }

            void Finish(bool ok, string message)
            {
                Debug.Log((ok ? "[Smoke] OK: " : "[Smoke] FAILED: ") + message);
                Application.Quit(ok ? 0 : 1);
            }
        }
    }
}
