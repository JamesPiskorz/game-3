using System;
using System.IO;
using Game.Characters.Content;

namespace Game.Characters.Tests.Generation
{
    /// <summary>
    /// Loads the project's real content catalogs, from the Unity project root or next to the test assembly: the stand-in
    /// catalog, and the one for the generated parts (ContentAuthoring.GeneratedContent writes it).
    /// </summary>
    static class Catalogs
    {
        const string ProjectPath = "Assets/_Game/Characters/Content/Catalog.json";
        const string GeneratedPath = "Assets/_Game/Characters/Content/GeneratedCatalog.json";

        static string _json, _generatedJson;

        public static string Json => _json ?? (_json = File.ReadAllText(Locate(ProjectPath)));

        /// <summary>A fresh copy each call, so tests can modify it.</summary>
        public static ContentCatalog Project() => CatalogSerializer.FromJson(Json);

        /// <summary>The generated parts' catalog: a fresh copy each call.</summary>
        public static ContentCatalog Generated() => CatalogSerializer.FromJson(_generatedJson ?? (_generatedJson = File.ReadAllText(Locate(GeneratedPath))));

        static string Locate(string projectPath)
        {
            foreach (string root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (string dir = root; dir != null; dir = Path.GetDirectoryName(dir))
                {
                    string candidate = Path.Combine(dir, projectPath);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            string beside = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(projectPath));
            if (File.Exists(beside)) return beside;
            throw new FileNotFoundException("Couldn't find " + projectPath);
        }
    }
}
