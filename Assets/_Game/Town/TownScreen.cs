using System;
using System.IO;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
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
    /// <see cref="PlayerSeed"/>.
    /// </summary>
    public sealed class TownScreen : MonoBehaviour
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
            View = new TownView(root, Session, Save, Load, ToMenu);
            if (ContinueOnStart)
            {
                ContinueOnStart = false;
                Load();
            }
            Stage.Show(Session);
            Stage.SnapCamera();
        }

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
    }
}
