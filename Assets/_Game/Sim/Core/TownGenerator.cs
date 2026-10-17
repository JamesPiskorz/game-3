using System;
using System.Collections.Generic;
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
    /// </summary>
    public sealed class TownGenerator
    {
        public const int DefaultPopulation = 24;

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

            // No job takes more than its share, so the café has staff and the shop isn't half the town.
            var jobs = new Dictionary<string, int>();
            int jobCap = Math.Max(2, (int)Math.Ceiling(population * 1.5 / Math.Max(1, _sim.Jobs.Count)));
            bool Full(JobEntry job) => jobs.TryGetValue(job.Id, out int count) && count >= jobCap;

            List<CharacterDna> people = new PopulationGenerator(_generator).Generate(worldSeed, population);
            for (int i = 0; i < people.Count; i++)
            {
                CharacterDna dna = people[i];
                ResidentProfile profile = profiles.Generate(dna);

                // Two residents sharing a full name would be confusing: reroll the family name until it's unique.
                Pcg32 rename = SeedStreams.For(dna.Seed, "profile.rename");
                for (int tries = 0; !names.Add(profile.Name) && tries < 50; tries++)
                    profile.FamilyName = rename.Pick(_sim.Names.Family);

                JobEntry job = _sim.Job(profile.JobId ?? "");
                if (job != null && Full(job)) profiles.ReassignJob(dna, profile, Full);
                if (profile.JobId != null) jobs[profile.JobId] = jobs.TryGetValue(profile.JobId, out int held) ? held + 1 : 1;

                world.Residents.Add(new Resident { Index = i, Dna = dna, Profile = profile, Schedule = schedules.Generate(dna.Seed, profile) });
            }
            world.Social = SocialWeb.Build(world.Residents, worldSeed);
            return world;
        }
    }
}
