using System.Collections.Generic;
using System.IO;
using Game.Characters.Building;
using Game.Characters.Dna;
using Game.Characters.Expressions;
using Game.Characters.Generation;
using Game.Characters.Modular;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.Town
{
    /// <summary>
    /// The front door: start a new game in the character creator, or continue the saved one in town. A fresh cast
    /// of generated characters stands behind the menu each visit, as a reminder of what the game is about.
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

        /// <summary>Set for deterministic captures; 0 picks a new cast every visit.</summary>
        public ulong CastSeed;

        public static bool HasSave => File.Exists(TownScreen.SavePath);

        void Start()
        {
            BuildCast();
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
            panel.Add(MenuButton("quit", "Quit", Application.Quit));
        }

        void BuildCast()
        {
            if (Content == null || Stage == null) return;
            var generator = new CharacterGenerator(Content.LoadCatalog());
            var builder = new CharacterBuilder(Content);
            ulong seed = CastSeed != 0 ? CastSeed : (ulong)System.DateTime.Now.Ticks;
            List<CharacterDna> cast = new PopulationGenerator(generator).Generate(seed, Cast);
            for (int i = 0; i < cast.Count; i++)
            {
                ModularCharacter character = builder.Build(cast[i], i % 2 == 0 ? "casual" : "date", Stage);
                float offset = i - (cast.Count - 1) / 2f;
                character.transform.localPosition = new Vector3(-offset * 0.95f, 0f, -0.18f * offset * offset);
                character.transform.localRotation = Quaternion.Euler(0f, offset * -9f, 0f);
                Poses.Relaxed(character.transform);
                character.GetComponent<FaceExpression>()?.Show(i % 3 == 0 ? "happy" : i % 3 == 1 ? "delighted" : "shy", 0.7f, float.PositiveInfinity);
            }
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
