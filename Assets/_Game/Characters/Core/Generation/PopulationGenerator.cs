using System.Collections.Generic;
using System.Linq;
using Game.Characters.Dna;
using Game.Characters.Rng;

namespace Game.Characters.Generation
{
    /// <summary>A generated town's people, and which family each belongs to.</summary>
    public sealed class Population
    {
        public List<CharacterDna> People = new List<CharacterDna>();

        /// <summary>Per person, the index of their family's first member (their own index if they have no family here).</summary>
        public List<int> Family = new List<int>();

        public IEnumerable<int> RelativesOf(int person) =>
            Enumerable.Range(0, People.Count).Where(i => i != person && Family[i] == Family[person]);
    }

    /// <summary>
    /// Generates a whole town's residents from one world seed with no lookalikes: a candidate is rejected when it
    /// is closer than <see cref="MinDistance"/> to anyone already accepted (PLAN.md §3.8, population mode).
    /// With a <see cref="FamilyShare"/>, some residents are siblings of someone generated before them
    /// (<see cref="FamilyResemblance"/>); they're meant to look related, so they only need to be half as far from family.
    /// </summary>
    public sealed class PopulationGenerator
    {
        public float MinDistance = 0.9f;
        public int MaxAttemptsPerResident = 30;

        /// <summary>The chance that each resident after the first is a sibling of an earlier one. 0 means no families.</summary>
        public float FamilyShare;
        public int MaxFamilySize = 3;

        readonly CharacterGenerator _generator;

        public PopulationGenerator(CharacterGenerator generator) => _generator = generator;

        public List<CharacterDna> Generate(ulong worldSeed, int count, GenerationRequest request = null) => GenerateTown(worldSeed, count, request).People;

        public Population GenerateTown(ulong worldSeed, int count, GenerationRequest request = null)
        {
            var town = new Population();
            var familySize = new Dictionary<int, int>();
            Pcg32 families = SeedStreams.For(worldSeed, "population.families");
            ulong candidate = 0;
            int attempts = 0;
            int relativeOf = -2; // -2: not decided yet for this place in the town
            while (town.People.Count < count)
            {
                int place = town.People.Count;
                if (relativeOf == -2)
                {
                    relativeOf = -1;
                    // Drawn only when families are on, so a town without them is exactly what it was before.
                    if (FamilyShare > 0f && place > 0 && families.Chance(FamilyShare))
                    {
                        List<int> open = Enumerable.Range(0, place).Where(i => familySize[town.Family[i]] < MaxFamilySize).ToList();
                        if (open.Count > 0) relativeOf = families.Pick(open);
                    }
                }

                ulong seed = StableHash.SplitMix64(worldSeed ^ StableHash.SplitMix64(candidate++));
                CharacterDna dna = relativeOf >= 0 ? _generator.GenerateRelative(town.People[relativeOf], seed, request) : _generator.Generate(seed, request);
                attempts++;

                // After too many rejections take the candidate anyway: a crowded catalog shouldn't hang world creation.
                int family = relativeOf >= 0 ? town.Family[relativeOf] : place;
                if (attempts < MaxAttemptsPerResident && !FarEnough(dna, town, family)) continue;
                town.People.Add(dna);
                town.Family.Add(family);
                familySize[family] = familySize.TryGetValue(family, out int size) ? size + 1 : 1;
                attempts = 0;
                relativeOf = -2;
            }
            return town;
        }

        bool FarEnough(CharacterDna candidate, Population town, int family)
        {
            for (int i = 0; i < town.People.Count; i++)
            {
                float needed = town.Family[i] == family ? MinDistance * 0.5f : MinDistance;
                if (AppearanceDistance.Between(candidate, town.People[i]) < needed) return false;
            }
            return true;
        }
    }
}
