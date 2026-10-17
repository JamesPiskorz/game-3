using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Rng;
using Game.Sim.Residents;
using Game.Sim.Social;

namespace Game.Sim
{
    /// <summary>
    /// Builds a town from one seed: a population with no lookalikes, then each resident's profile and weekly
    /// schedule from their own character seed. The same seed and content always produce the same town.
    /// About a quarter of residents are a sibling of someone else in town: they look related
    /// (<see cref="FamilyResemblance"/>), share a family name and are close in age. Every other family name is unique,
    /// so a shared surname always means family.
    /// </summary>
    public sealed class TownGenerator
    {
        public const int DefaultPopulation = 24;
        public const float FamilyShare = 0.25f;
        public const int SiblingAgeGap = 6;

        readonly SimCatalog _sim;
        readonly ContentCatalog _characters;
        readonly CharacterGenerator _generator;

        public TownGenerator(SimCatalog sim, ContentCatalog characters)
        {
            _sim = sim;
            _characters = characters;
            _generator = new CharacterGenerator(characters);
        }

        public SimWorld Create(ulong worldSeed, int population = DefaultPopulation)
        {
            var world = new SimWorld { Seed = worldSeed, Catalog = _sim, Characters = _characters };
            var profiles = new ProfileGenerator(_sim, _characters);
            var schedules = new ScheduleGenerator(_sim);
            var names = new HashSet<string>();
            var firstNames = new HashSet<string>();
            var familyNames = new HashSet<string>();

            // No job takes more than its share, so the café has staff and the shop isn't half the town.
            var jobs = new Dictionary<string, int>();
            int jobCap = Math.Max(2, (int)Math.Ceiling(population * 1.5 / Math.Max(1, _sim.Jobs.Count)));
            bool Full(JobEntry job) => jobs.TryGetValue(job.Id, out int count) && count >= jobCap;

            Population town = new PopulationGenerator(_generator) { FamilyShare = FamilyShare }.GenerateTown(worldSeed, population);
            List<CharacterDna> people = town.People;
            for (int i = 0; i < people.Count; i++)
            {
                CharacterDna dna = people[i];
                ResidentProfile profile = profiles.Generate(dna);
                int family = town.Family[i];

                // Dialogue, the diary and the contact book use first names, so two Diegos in one town would be
                // confusing: reroll from the same pool until the first name is free, then make the full name unique.
                Pcg32 rename = SeedStreams.For(dna.Seed, "profile.rename");
                List<string> pool = new[] { _sim.Names.Feminine, _sim.Names.Masculine, _sim.Names.Neutral }.First(p => p.Contains(profile.FirstName));
                for (int tries = 0; firstNames.Contains(profile.FirstName) && tries < 50; tries++)
                    profile.FirstName = rename.Pick(pool);
                firstNames.Add(profile.FirstName);
                if (family != i)
                {
                    // A sibling: the family's name, and an age a few years either side of theirs.
                    ResidentProfile relative = world.Residents[family].Profile;
                    profile.FamilyName = relative.FamilyName;
                    Pcg32 kin = SeedStreams.For(dna.Seed, "profile.family");
                    int gap = kin.Range(1, SiblingAgeGap + 1) * (kin.Chance(0.5f) ? 1 : -1);
                    profile.Age = Math.Max(ProfileGenerator.MinAge, Math.Min(ProfileGenerator.MaxAge, relative.Age + gap));
                    if (profile.JobId == "student" && profile.Age > 25) profiles.ReassignJob(dna, profile, Full);
                }
                else
                {
                    // Someone without family here takes a name no other family has, while there are names to go round.
                    for (int tries = 0; familyNames.Contains(profile.FamilyName) && tries < 50; tries++)
                        profile.FamilyName = rename.Pick(_sim.Names.Family);
                }
                familyNames.Add(profile.FamilyName);
                for (int tries = 0; !names.Add(profile.Name) && tries < 50; tries++)
                    profile.FamilyName = rename.Pick(_sim.Names.Family);

                JobEntry job = _sim.Job(profile.JobId ?? "");
                if (job != null && Full(job)) profiles.ReassignJob(dna, profile, Full);
                if (profile.JobId != null) jobs[profile.JobId] = jobs.TryGetValue(profile.JobId, out int held) ? held + 1 : 1;

                world.Residents.Add(new Resident { Index = i, Dna = dna, Profile = profile, Schedule = schedules.Generate(dna.Seed, profile), Family = family });
            }
            world.Social = SocialWeb.Build(world.Residents, worldSeed);
            return world;
        }
    }
}
