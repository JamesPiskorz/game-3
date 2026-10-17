using System;
using System.IO;
using Game.Characters.Content;

namespace Game.Characters.Tests.Generation
{
    /// <summary>Loads the project's real content catalog, from the Unity project root or next to the test assembly.</summary>
    static class Catalogs
    {
        const string ProjectPath = "Assets/_Game/Characters/Content/Catalog.json";

        static string _json;

        public static string Json => _json ?? (_json = File.ReadAllText(Locate()));

        /// <summary>A fresh copy each call, so tests can modify it.</summary>
        public static ContentCatalog Project() => CatalogSerializer.FromJson(Json);

        static string Locate()
        {
            foreach (string root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (string dir = root; dir != null; dir = Path.GetDirectoryName(dir))
                {
                    string candidate = Path.Combine(dir, ProjectPath);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            string beside = Path.Combine(AppContext.BaseDirectory, "Catalog.json");
            if (File.Exists(beside)) return beside;
            throw new FileNotFoundException("Couldn't find " + ProjectPath);
        }
    }
}
