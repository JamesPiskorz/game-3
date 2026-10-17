using System.Collections.Generic;
using Game.Characters.Dna;
using Game.Characters.Rng;

namespace Game.Characters.Generation
{
    /// <summary>
    /// Generates a whole town's residents from one world seed with no lookalikes: a candidate is rejected when it
    /// is closer than <see cref="MinDistance"/> to anyone already accepted (PLAN.md §3.8, population mode).
    /// </summary>
    public sealed class PopulationGenerator
    {
        public float MinDistance = 0.9f;
        public int MaxAttemptsPerResident = 30;

        readonly CharacterGenerator _generator;

        public PopulationGenerator(CharacterGenerator generator) => _generator = generator;

        public List<CharacterDna> Generate(ulong worldSeed, int count, GenerationRequest request = null)
        {
            var residents = new List<CharacterDna>(count);
            ulong candidate = 0;
            int attempts = 0;
            while (residents.Count < count)
            {
                ulong seed = StableHash.SplitMix64(worldSeed ^ StableHash.SplitMix64(candidate++));
                CharacterDna dna = _generator.Generate(seed, request);
                attempts++;

                // After too many rejections take the candidate anyway: a crowded catalog shouldn't hang world creation.
                if (attempts < MaxAttemptsPerResident && !FarEnough(dna, residents)) continue;
                residents.Add(dna);
                attempts = 0;
            }
            return residents;
        }

        bool FarEnough(CharacterDna candidate, List<CharacterDna> residents)
        {
            foreach (CharacterDna resident in residents)
                if (AppearanceDistance.Between(candidate, resident) < MinDistance) return false;
            return true;
        }
    }
}
