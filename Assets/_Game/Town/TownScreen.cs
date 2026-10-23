using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Feedback;
using Game.Sim;
using Game.Sim.Sandbox;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Town
{
    /// <summary>
    /// Runs the sandbox prototype: generates the town, puts the player in it and connects the
    /// <see cref="TownSession"/> to the HUD (<see cref="TownView"/>) and the 3D stage (<see cref="TownStage"/>).
    /// The player's look comes from the character creator (<see cref="PlayerLook"/>) when one was saved, else from
    /// <see cref="PlayerSeed"/>. Feedback tickets (F8) get the town's state from it, and its save.
    /// </summary>
    public sealed class TownScreen : MonoBehaviour, IFeedbackSource
    {
        public CharacterContent Content;
        public TextAsset SimCatalogJson;
        public UIDocument Document;
        public StyleSheet Style;
        public TownStage Stage;

        public ulong WorldSeed = 0x5EED;
        public int Population = TownGenerator.DefaultPopulation;
        public ulong PlayerSeed = 7;
        public string PlayerName = "Kit";
        public int StartDay = 5;
        public int StartHour = 11;
        public string StartLocation = "cafe";

        SimCatalog _sim;
        ContentCatalog _characters;

        public TownSession Session { get; private set; }
        public TownView View { get; private set; }

        public static string SavePath => Path.Combine(Application.persistentDataPath, "town-save.json");

        /// <summary>Set by the main menu's Continue: load the save as soon as the town starts.</summary>
        public static bool ContinueOnStart;

        /// <summary>Off for automated runs (the player smoke test), so they never touch a real player's save.</summary>
        public static bool SavingEnabled = true;

        void Start()
        {
            _characters = Content.LoadCatalog();
            _sim = SimCatalog.FromJson(SimCatalogJson.text);

            SimWorld world = new TownGenerator(_sim, _characters).Create(WorldSeed, Population);
            world.Now = SimTime.At(StartDay, StartHour);
            world.Player.Name = PlayerLook.Name ?? PlayerName;
            world.Player.Location = StartLocation;
            world.Player.Dna = PlayerDna();

            Session = new TownSession(world);
            Session.Changed += () => Stage.Show(Session);
            Session.Slept += Autosave;

            VisualElement root = Document.rootVisualElement;
            if (Style != null) root.styleSheets.Add(Style);
            _portraits = new Portraits(new Game.Characters.Building.CharacterBuilder(Content), Stage.Sun);
            View = new TownView(root, Session, Save, Load, ToMenu, r => _portraits.For(r),
                () => _portraits.ForPlayer(Session.World.Player.Dna, Session.World.Player.LookVersion), p => _portraits.ForPhoto(p));
            if (ContinueOnStart)
            {
                ContinueOnStart = false;
                Load();
            }
            Stage.Show(Session);
            Stage.SnapCamera();
        }

        Portraits _portraits;

        void OnDestroy() => _portraits?.Dispose();

        void LateUpdate()
        {
            if (View != null && Stage != null) View.PlaceBubbles(Stage.HeadPosition, Stage.Camera);
        }

        CharacterDna PlayerDna() => PlayerLook.TryLoad(out CharacterDna dna) ? dna : new CharacterGenerator(_characters).Generate(PlayerSeed);

        void Save()
        {
            if (!SavingEnabled) return;
            File.WriteAllText(SavePath, SimSave.Capture(Session.World).ToJson());
            Session.Tell("Saved.");
        }

        /// <summary>Back to the main menu, saving first so Continue picks up here.</summary>
        void ToMenu()
        {
            Autosave();
            UnityEngine.SceneManagement.SceneManager.LoadScene(MainMenuScreen.MenuScene);
        }

        /// <summary>Every night, quietly. Load picks up the morning after.</summary>
        void Autosave()
        {
            if (!SavingEnabled) return;
            try
            {
                File.WriteAllText(SavePath, SimSave.Capture(Session.World).ToJson());
            }
            catch (IOException e)
            {
                Debug.LogWarning("[Town] Autosave failed: " + e.Message);
            }
        }

        void Load()
        {
            if (!File.Exists(SavePath))
            {
                Session.Tell("There's no save yet.");
                return;
            }
            try
            {
                Session.Load(SimSave.FromJson(File.ReadAllText(SavePath)).Restore(_sim, _characters));
                Session.Tell("Loaded: " + Session.World.Now + ".");
            }
            catch (FormatException e)
            {
                Session.Tell("Couldn't load the save: " + e.Message);
            }
        }

        // ---------------------------------------------------------------- feedback tickets

        string IFeedbackSource.FeedbackSection => "town";

        object IFeedbackSource.DescribeForFeedback()
        {
            if (Session == null) return null;
            SimWorld world = Session.World;
            string here = Session.Location;
            return new
            {
                worldSeed = world.Seed.ToString("x"),
                now = world.Now.ToString(),
                weather = world.Weather.ToString(),
                location = here,
                place = Session.LocationName(here),
                open = Session.IsOpen(here),
                occasion = TownEvents.At(world, here, world.Now)?.Name,
                eventsToday = TownEvents.On(world, world.Now.Day).Select(e => new { e.Name, e.Location, e.Start, e.End }),
                player = new
                {
                    world.Player.Name, world.Player.Location, world.Player.Outfit, outfitHere = world.OutfitAt(here),
                    world.Player.Money, world.Player.Stats, world.Player.LookVersion,
                    seed = world.Player.Dna?.Seed.ToString("x"), wardrobe = world.Player.Wardrobe.Count,
                },
                selected = Session.Selected?.ToString(),
                dateWith = Session.DateWith?.ToString(),
                speaker = Session.Speaker?.ToString(),
                notice = Session.Notice,
                hint = Session.Hint(),
                present = Session.Present().Select(r => new { r.Index, r.Name, seed = r.Dna.Seed.ToString("x"), outfit = world.OutfitFor(r, here) }),
                actions = world.Actions,
            };
        }

        IEnumerable<(string Label, string Value)> IFeedbackSource.FeedbackSummary
        {
            get
            {
                if (Session == null) yield break;
                string occasion = TownEvents.At(Session.World, Session.Location, Session.World.Now)?.Name;
                yield return ("Game time", $"{Session.World.Now}, {Session.World.Weather}");
                yield return ("Place", Session.LocationName(Session.Location) + (occasion != null ? $" ({occasion})" : ""));
                if (Session.Selected != null) yield return ("With", Session.Selected.Name);
                yield return ("World seed", Session.World.Seed.ToString("x"));
            }
        }

        IReadOnlyDictionary<ulong, string> IFeedbackSource.FeedbackNames
        {
            get
            {
                if (Session == null) return null;
                var names = new Dictionary<ulong, string>();
                foreach (Resident resident in Session.World.Residents.Where(r => r.Dna != null)) names[resident.Dna.Seed] = resident.Name;
                if (Session.World.Player.Dna != null) names[Session.World.Player.Dna.Seed] = Session.World.Player.Name + " (the player)";
                return names;
            }
        }

        /// <summary>The town saved as it is: load it to stand where the ticket was made.</summary>
        IReadOnlyDictionary<string, string> IFeedbackSource.FeedbackAttachments =>
            Session == null ? null : new Dictionary<string, string> { ["town-save.json"] = SimSave.Capture(Session.World).ToJson() };
    }
}
