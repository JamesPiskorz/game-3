using System;
using System.IO;
using Game.Characters.Content;
using Game.Sim.Residents;

namespace Game.Sim.Tests
{
    /// <summary>The project's real catalogs, found from the Unity project root or next to the test assembly.</summary>
    static class Fixtures
    {
        static string _simJson, _characterJson, _generatedJson;

        public static SimCatalog Sim() => SimCatalog.FromJson(_simJson ?? (_simJson = Read("Assets/_Game/Sim/Content/SimCatalog.json")));

        public static ContentCatalog Characters() =>
            CatalogSerializer.FromJson(_characterJson ?? (_characterJson = Read("Assets/_Game/Characters/Content/Catalog.json")));

        public static SimWorld Town(ulong seed = 0x5EED, int population = 24) => new TownGenerator(Sim(), Characters()).Create(seed, population);

        /// <summary>The generated content's catalog (decision 0008), which adds nightwear, swimwear and formal wear.</summary>
        public static ContentCatalog GeneratedCharacters() =>
            CatalogSerializer.FromJson(_generatedJson ?? (_generatedJson = Read("Assets/_Game/Characters/Content/GeneratedCatalog.json")));

        /// <summary>A town dressed from the generated content.</summary>
        public static SimWorld GeneratedTown(ulong seed = 0x5EED, int population = 24) =>
            new TownGenerator(Sim(), GeneratedCharacters()).Create(seed, population);

        /// <summary>A world with one hand-made resident whose tastes the test controls. No player look, so no impression.</summary>
        public static (SimWorld World, Resident Resident) OneResident(Action<ResidentProfile> setup = null)
        {
            SimWorld world = Town(0xAB, 1);
            Resident resident = world.Residents[0];
            ResidentProfile profile = resident.Profile;
            profile.Interests.Clear();
            profile.Interests["music"] = 0.9f;
            profile.Interests["books"] = 0.8f;
            profile.Interests["sports"] = -0.9f;
            profile.Personality = new Personality();
            setup?.Invoke(profile);
            return (world, resident);
        }

        static string Read(string projectPath)
        {
            foreach (string root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (string dir = root; dir != null; dir = Path.GetDirectoryName(dir))
                {
                    string candidate = Path.Combine(dir, projectPath);
                    if (File.Exists(candidate)) return File.ReadAllText(candidate);
                }
            string beside = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(projectPath));
            if (File.Exists(beside)) return File.ReadAllText(beside);
            throw new FileNotFoundException("Couldn't find " + projectPath);
        }
    }
}
