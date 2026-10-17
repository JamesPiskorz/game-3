using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Expressions;
using Game.Characters.Generation;
using Game.Characters.Modular;
using Game.Characters.Rng;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.Town
{
    /// <summary>
    /// The front door: start a new game in the character creator, or continue the saved one in town. Behind the menu
    /// stands a cast of generated characters, and every few seconds one of them makes way for someone new (with a
    /// caption saying what the generator picked), as a reminder of what the game is about.
    /// </summary>
    public sealed class MainMenuScreen : MonoBehaviour
    {
        public const string CreatorScene = "Creator";
        public const string TownScene = "Town";
        public const string MenuScene = "MainMenu";

        public CharacterContent Content;
        public UIDocument Document;
        public StyleSheet Style;
        public Transform Stage;
        public int Cast = 5;

        /// <summary>Seconds between one cast member making way for a new face.</summary>
        public float SwapSeconds = 4.5f;

        /// <summary>Set for deterministic captures; 0 picks a new cast every visit.</summary>
        public ulong CastSeed;

        const float PopSeconds = 0.45f;
        static readonly int[] SwapOrder = { 2, 0, 4, 1, 3 };
        static readonly string[] Moods = { "happy", "delighted", "shy" };

        public static bool HasSave => File.Exists(TownScreen.SavePath);

        ContentCatalog _catalog;
        CharacterGenerator _generator;
        CharacterBuilder _builder;
        ulong _seed;
        int _made, _swaps;
        float _nextSwap;
        readonly List<ModularCharacter> _cast = new List<ModularCharacter>();
        readonly List<float> _popStart = new List<float>();
        Label _caption;

        /// <summary>How many new faces have stepped in since the menu opened.</summary>
        public int Swaps => _swaps;

        void Start()
        {
            VisualElement root = Document.rootVisualElement;
            if (Style != null) root.styleSheets.Add(Style);
            root.AddToClassList("menu");

            var panel = new VisualElement();
            panel.AddToClassList("menu-panel");
            root.Add(panel);
            panel.Add(Label("Sandbox dating sim", "menu-title"));
            panel.Add(Label("Prototype build: stand-in art, generated town", "menu-subtitle"));

            Button newGame = MenuButton("new-game", "New game", () => SceneManager.LoadScene(CreatorScene));
            newGame.AddToClassList("primary");
            panel.Add(newGame);
            Button resume = MenuButton("continue", "Continue", () =>
            {
                TownScreen.ContinueOnStart = true;
                SceneManager.LoadScene(TownScene);
            });
            resume.SetEnabled(HasSave);
            panel.Add(resume);
            LastPhoto(panel);
            panel.Add(MenuButton("quit", "Quit", Application.Quit));

            _caption = Label("Everyone here was just generated: face, hair, skin and clothes.", "menu-caption");
            root.Add(_caption);
            var shuffle = new Button(Reshuffle) { name = "shuffle", text = "New faces" };
            shuffle.AddToClassList("menu-shuffle");
            root.Add(shuffle);

            BuildCast();
        }

        PortraitStudio _studio;
        RenderTexture _photo;

        /// <summary>With a saved game, the most recent date photo under Continue: a reason to come back.</summary>
        void LastPhoto(VisualElement panel)
        {
            if (!HasSave || Content == null) return;
            Game.Sim.Relationships.Photo last;
            try
            {
                last = Game.Sim.SimSave.FromJson(File.ReadAllText(TownScreen.SavePath)).Photos?.LastOrDefault();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Menu] Couldn't read the save for its photos: {e.Message}");
                return;
            }
            if (last?.PlayerLook == null || last.TheirLook == null) return;
            _studio = new PortraitStudio(new CharacterBuilder(Content), new Color(0.86f, 0.8f, 0.88f));
            _photo = PortraitStudio.Target(360, 240);
            _studio.RenderPair(DnaSerializer.FromJson(last.PlayerLook), DnaSerializer.FromJson(last.TheirLook), Game.Sim.Sandbox.TownSession.DateOutfit, _photo);
            var picture = new VisualElement { name = "last-photo" };
            picture.AddToClassList("menu-photo");
            picture.style.backgroundImage = Background.FromRenderTexture(_photo);
            panel.Add(picture);
            string with = last.Name != null ? $" with {last.Name}" : "";
            string where = last.Place != null ? $" at {last.Place}" : "";
            panel.Add(Label($"Last time: day {last.Day + 1}{with}{where}.", "menu-subtitle"));
        }

        void OnDestroy()
        {
            _studio?.Dispose();
            if (_photo != null)
            {
                _photo.Release();
                Destroy(_photo);
            }
        }

        void BuildCast()
        {
            if (Content == null || Stage == null) return;
            _catalog = Content.LoadCatalog();
            _generator = new CharacterGenerator(_catalog);
            _builder = new CharacterBuilder(Content);
            _seed = CastSeed != 0 ? CastSeed : (ulong)System.DateTime.Now.Ticks;
            List<CharacterDna> cast = new PopulationGenerator(_generator).Generate(_seed, Cast);
            for (int i = 0; i < cast.Count; i++)
            {
                _cast.Add(Place(cast[i], i, cast.Count));
                _popStart.Add(-1f);
            }
            _nextSwap = Time.time + SwapSeconds;
        }

        /// <summary>Builds a cast member on their mark: a shallow arc facing the camera.</summary>
        ModularCharacter Place(CharacterDna dna, int slot, int count)
        {
            ModularCharacter character = _builder.Build(dna, slot % 2 == 0 ? "casual" : "date", Stage);
            float offset = slot - (count - 1) / 2f;
            character.transform.localPosition = new Vector3(-offset * 0.95f, 0f, -0.18f * offset * offset);
            character.transform.localRotation = Quaternion.Euler(0f, offset * -9f, 0f);
            Poses.Relaxed(character.transform);
            character.GetComponent<FaceExpression>()?.Show(Moods[slot % Moods.Length], 0.7f, float.PositiveInfinity);
            return character;
        }

        void Update()
        {
            if (_cast.Count == 0) return;
            for (int i = 0; i < _cast.Count; i++)
            {
                if (_popStart[i] < 0f) continue;
                float t = (Time.time - _popStart[i]) / PopSeconds;
                if (t >= 1f)
                {
                    _popStart[i] = -1f;
                    t = 1f;
                }
                _cast[i].transform.localScale = Vector3.one * Pop(t);
            }
            if (SwapSeconds > 0f && Time.time >= _nextSwap) Swap();
        }

        /// <summary>One cast member makes way for a newly generated face, who arrives pleased to be here; the neighbours look surprised.</summary>
        public void Swap()
        {
            if (_generator == null) return;
            _nextSwap = Time.time + SwapSeconds;
            int slot = SwapOrder[_swaps % SwapOrder.Length] % _cast.Count;
            _swaps++;
            CharacterDna dna = _generator.Generate(StableHash.SplitMix64(_seed ^ StableHash.SplitMix64((ulong)++_made)));
            Destroy(_cast[slot].gameObject);
            ModularCharacter character = Place(dna, slot, _cast.Count);
            character.transform.localScale = Vector3.one * Pop(0f);
            character.GetComponent<FaceExpression>()?.Show("delighted", 1f, 1.8f);
            _cast[slot] = character;
            _popStart[slot] = Time.time;
            foreach (int neighbour in new[] { slot - 1, slot + 1 })
                if (neighbour >= 0 && neighbour < _cast.Count)
                    _cast[neighbour].GetComponent<FaceExpression>()?.Show("surprised", 0.6f, 1.2f);
            _caption.text = Describe(dna);
        }

        /// <summary>A whole new cast at once.</summary>
        void Reshuffle()
        {
            if (_generator == null) return;
            foreach (ModularCharacter character in _cast) Destroy(character.gameObject);
            _cast.Clear();
            _popStart.Clear();
            _seed = StableHash.SplitMix64(_seed ^ 0x5EEDu);
            List<CharacterDna> cast = new PopulationGenerator(_generator).Generate(_seed, Cast);
            for (int i = 0; i < cast.Count; i++)
            {
                _cast.Add(Place(cast[i], i, cast.Count));
                _cast[i].transform.localScale = Vector3.one * Pop(0f);
                _popStart.Add(Time.time + i * 0.08f);
            }
            _nextSwap = Time.time + SwapSeconds;
            _caption.text = "A whole new cast, generated just now.";
        }

        /// <summary>"New face  ·  Gothic style  ·  Hair: twin tails, blunt bangs  ·  Wearing: blouse, jacket".</summary>
        string Describe(CharacterDna dna)
        {
            string Names(IEnumerable<string> ids, params string[] slots)
            {
                var names = new List<string>();
                foreach (string slot in slots)
                    foreach (string id in ids)
                        if (_catalog.Part(id)?.Slot == slot) names.Add(_catalog.Part(id).Name.ToLowerInvariant());
                return string.Join(", ", names);
            }
            OutfitDna outfit = dna.Outfits.Find(o => o.Context == "casual") ?? (dna.Outfits.Count > 0 ? dna.Outfits[0] : null);
            string hair = Names(dna.Hair.PieceIds, Slots.HairBack, Slots.HairFront);
            string wearing = outfit == null ? "" : Names(outfit.Items.ConvertAll(i => i.ItemId), Slots.OnePiece, Slots.Top, Slots.Outer);
            string style = string.IsNullOrEmpty(dna.Style) ? "Their own" : char.ToUpperInvariant(dna.Style[0]) + dna.Style.Substring(1);
            string text = $"New face  ·  {style} style";
            if (hair.Length > 0) text += "  ·  Hair: " + hair;
            if (wearing.Length > 0) text += "  ·  Wearing: " + wearing;
            return text;
        }

        /// <summary>A scale that overshoots a little and settles, 0..1.</summary>
        static float Pop(float t)
        {
            t = Mathf.Clamp01(t);
            const float back = 1.7f;
            float u = t - 1f;
            return Mathf.Max(0.01f, 1f + (back + 1f) * u * u * u + back * u * u);
        }

        static Label Label(string text, string style)
        {
            var label = new Label(text);
            label.AddToClassList(style);
            return label;
        }

        static Button MenuButton(string name, string text, System.Action clicked)
        {
            var button = new Button(clicked) { name = name, text = text };
            button.AddToClassList("menu-button");
            return button;
        }
    }
}
