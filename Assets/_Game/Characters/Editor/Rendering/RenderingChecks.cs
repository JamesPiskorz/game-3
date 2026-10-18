using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.Rendering
{
    /// <summary>
    /// Scripted checks on the rendering setup. Graphics plan G0.6: no material a game scene uses may be URP Lit or
    /// Simple Lit (sets are toon-shaded so they match the characters).
    /// Headless: -executeMethod Game.Characters.Rendering.RenderingChecks.Run (exits 0 pass, 1 fail).
    /// </summary>
    public static class RenderingChecks
    {
        static readonly string[] GameScenes =
        {
            "Assets/_Game/Town/MainMenu.unity", "Assets/_Game/Creator/Creator.unity", "Assets/_Game/Town/Town.unity",
        };

        [MenuItem("Game/Rendering/Run Rendering Checks")]
        public static void Run()
        {
            List<string> lit = LitMaterials();
            bool pass = lit.Count == 0;
            Debug.Log(pass ? "[RenderingChecks] PASS: no URP Lit materials in the game scenes."
                           : "[RenderingChecks] FAIL: URP Lit materials in the game scenes: " + string.Join(", ", lit));
            if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1);
        }

        /// <summary>Materials the game scenes depend on (directly or through their assets) that use URP's Lit shaders.</summary>
        public static List<string> LitMaterials() =>
            GameScenes.SelectMany(scene => AssetDatabase.GetDependencies(scene, true))
                .Distinct()
                .Where(path => path.EndsWith(".mat"))
                .Select(path => (path, material: AssetDatabase.LoadAssetAtPath<Material>(path)))
                .Where(m => m.material != null && m.material.shader != null &&
                            (m.material.shader.name == "Universal Render Pipeline/Lit" || m.material.shader.name == "Universal Render Pipeline/Simple Lit"))
                .Select(m => m.path)
                .ToList();
    }
}
