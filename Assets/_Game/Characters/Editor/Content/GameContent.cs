using System.IO;
using Game.Characters.Building;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Characters.ContentAuthoring
{
    /// <summary>
    /// Which character content the game runs on: the stand-ins (Content/CharacterContent.asset, the default) or the
    /// generated body and parts (<see cref="GeneratedContent"/>). The choice is kept in ProjectSettings/GameContent.txt,
    /// so scene setup (TownSetup, CreatorSetup) keeps it, and switching repoints every CharacterContent field in the
    /// main menu, town and creator scenes. Tests and the review sheets don't depend on it.
    /// Headless: unity run . -- -executeMethod Game.Characters.ContentAuthoring.GameContent.UseGenerated (or UseStandIn)
    /// </summary>
    public static class GameContent
    {
        const string ChoicePath = "ProjectSettings/GameContent.txt";
        const string GeneratedChoice = "generated";
        const string GeneratedMenu = "Game/Characters/Content/Game Uses Generated Content";
        const string StandInMenu = "Game/Characters/Content/Game Uses Stand-in Content";

        public static readonly string[] Scenes =
        {
            "Assets/_Game/Town/MainMenu.unity",
            "Assets/_Game/Town/Town.unity",
            "Assets/_Game/Creator/Creator.unity",
        };

        public static bool UsesGenerated => File.Exists(ChoicePath) && File.ReadAllText(ChoicePath).Trim() == GeneratedChoice;

        /// <summary>The content the game scenes should use, written first if it's the generated one and it's missing.</summary>
        public static CharacterContent Active() => UsesGenerated ? GeneratedContent.Load() ?? GeneratedContent.Write() : StandInContent.Load();

        [MenuItem(GeneratedMenu, priority = 20)]
        public static void UseGenerated() => Use(true);

        [MenuItem(StandInMenu, priority = 21)]
        public static void UseStandIn() => Use(false);

        [MenuItem(GeneratedMenu, true)]
        static bool UseGeneratedCheck()
        {
            Menu.SetChecked(GeneratedMenu, UsesGenerated);
            return true;
        }

        [MenuItem(StandInMenu, true)]
        static bool UseStandInCheck()
        {
            Menu.SetChecked(StandInMenu, !UsesGenerated);
            return true;
        }

        public static void Use(bool generated)
        {
            CharacterContent content = generated ? GeneratedContent.Write() : StandInContent.Load();
            if (content == null)
            {
                Debug.LogError("[Content] No content to switch to; the scenes are unchanged.");
                return;
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string previous = SceneManager.GetActiveScene().path;

            // Loaded again after each scene opens: opening a scene unloads assets nothing references, and assigning the
            // unloaded content left the fields empty.
            string contentPath = AssetDatabase.GetAssetPath(content);
            int fields = 0;
            foreach (string path in Scenes)
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                content = AssetDatabase.LoadAssetAtPath<CharacterContent>(contentPath);
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                        fields += Repoint(behaviour, content);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            File.WriteAllText(ChoicePath, generated ? GeneratedChoice : "standin");
            if (!string.IsNullOrEmpty(previous) && !Application.isBatchMode) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            Debug.Log($"[Content] The game now uses {contentPath} ({fields} fields in {Scenes.Length} scenes).");
        }

        /// <summary>Points every CharacterContent field of <paramref name="behaviour"/> at <paramref name="content"/>.</summary>
        static int Repoint(MonoBehaviour behaviour, CharacterContent content)
        {
            if (behaviour == null) return 0;
            var serialized = new SerializedObject(behaviour);
            SerializedProperty property = serialized.GetIterator();
            int changed = 0;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (!(property.objectReferenceValue is CharacterContent) || property.objectReferenceValue == content) continue;
                property.objectReferenceValue = content;
                changed++;
            }
            if (changed > 0) serialized.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }
    }
}
