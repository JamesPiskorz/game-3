using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Rng;

namespace Game.Sim.Residents
{
    /// <summary>
    /// Derives a resident's profile from their character DNA. Everything comes from the "profile" seed stream, so
    /// appearance and personality never disturb each other's draws, and the same seed always yields the same person.
    /// The look feeds the profile: presentation picks the name pool, the dress archetype sets personality and tastes.
    /// </summary>
    public sealed class ProfileGenerator
    {
        public const int MinAge = 21;
        public const int MaxAge = 34;
        public const int Likes = 3;
        public const float LikeThreshold = 0.5f;

        // How far a trait strays from its archetype's mean (one standard deviation).
        const float TraitSpread = 0.075f;

        readonly SimCatalog _sim;
        readonly ContentCatalog _characters;

        public ProfileGenerator(SimCatalog sim, ContentCatalog characters)
        {
            _sim = sim;
            _characters = characters;
        }

        public ResidentProfile Generate(CharacterDna dna)
        {
            Pcg32 rng = SeedStreams.For(dna.Seed, "profile");
            var profile = new ResidentProfile { Archetype = dna.Style };

            Name(profile, dna.Body.Femininity, rng);
            profile.Age = rng.Range(MinAge, MaxAge + 1);

            _sim.Archetypes.TryGetValue(dna.Style ?? "", out ArchetypeTendencies tendencies);
            tendencies = tendencies ?? new ArchetypeTendencies();
            profile.Personality = Vary(tendencies.Traits, rng);

            Interests(profile, tendencies, rng);
            profile.JobId = Job(profile, tendencies, rng);
            profile.Hangouts = Hangouts(profile, tendencies);
            profile.StyleTaste = StyleTaste(dna.Style, rng);
            return profile;
        }

        /// <summary>
        /// Presentation sets the odds, never the outcome: feminine-presenting residents usually have names from the
        /// feminine pool and she/her, but some have neutral names or use they/them, and androgynous residents mostly
        /// draw neutral names.
        /// </summary>
        void Name(ResidentProfile profile, float femininity, Pcg32 rng)
        {
            NamePool names = _sim.Names;
            float roll = rng.NextFloat();
            List<string> pool;
            Pronouns usual;
            if (femininity >= 0.65f)
            {
                pool = roll < 0.85f ? names.Feminine : names.Neutral;
                usual = Pronouns.She;
            }
            else if (femininity <= 0.35f)
            {
                pool = roll < 0.85f ? names.Masculine : names.Neutral;
                usual = Pronouns.He;
            }
            else
            {
                pool = roll < 0.6f ? names.Neutral : roll < 0.8f ? names.Feminine : names.Masculine;
                usual = femininity >= 0.5f ? Pronouns.She : Pronouns.He;
            }

            profile.FirstName = rng.Pick(pool);
            profile.FamilyName = rng.Pick(names.Family);

            float theyChance = pool == names.Neutral ? 0.45f : 0.08f;
            profile.Pronouns = rng.Chance(theyChance) ? Pronouns.They : usual;
        }

        static Personality Vary(Personality mean, Pcg32 rng) => new Personality
        {
            Openness = Personality.Clamp01(mean.Openness + rng.NextNormal() * TraitSpread),
            Conscientiousness = Personality.Clamp01(mean.Conscientiousness + rng.NextNormal() * TraitSpread),
            Extraversion = Personality.Clamp01(mean.Extraversion + rng.NextNormal() * TraitSpread),
            Agreeableness = Personality.Clamp01(mean.Agreeableness + rng.NextNormal() * TraitSpread),
            Neuroticism = Personality.Clamp01(mean.Neuroticism + rng.NextNormal() * TraitSpread),
        };

        /// <summary>
        /// Three loves, one or two dislikes, the rest lukewarm. Archetype interests tilt which topics win, with enough
        /// noise that a sporty resident can still be the one who loves books.
        /// </summary>
        void Interests(ResidentProfile profile, ArchetypeTendencies tendencies, Pcg32 rng)
        {
            var scored = new List<(string Id, float Score)>();
            foreach (TopicEntry topic in _sim.Topics)
            {
                tendencies.Interests.TryGetValue(topic.Id, out float weight);
                scored.Add((topic.Id, weight / 3f + rng.NextNormal() * 0.3f));
            }
            scored.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : string.CompareOrdinal(a.Id, b.Id));

            int dislikes = rng.Chance(0.5f) ? 2 : 1;
            for (int i = 0; i < scored.Count; i++)
            {
                float value;
                if (i < Likes) value = 0.6f + 0.4f * rng.NextFloat();
                else if (i >= scored.Count - dislikes) value = -0.6f - 0.4f * rng.NextFloat();
                else value = (rng.NextFloat() - 0.5f) * 0.6f;
                profile.Interests[scored[i].Id] = value;
            }
        }

        /// <summary>
        /// Picks a different job when the town has enough people in theirs. Uses its own stream, so it doesn't
        /// change anything else about them.
        /// </summary>
        public void ReassignJob(CharacterDna dna, ResidentProfile profile, Func<JobEntry, bool> full)
        {
            _sim.Archetypes.TryGetValue(dna.Style ?? "", out ArchetypeTendencies tendencies);
            string job = Job(profile, tendencies ?? new ArchetypeTendencies(), SeedStreams.For(dna.Seed, "profile.job"), full);
            if (job != null) profile.JobId = job;
        }

        string Job(ResidentProfile profile, ArchetypeTendencies tendencies, Pcg32 rng, Func<JobEntry, bool> excluded = null)
        {
            if (_sim.Jobs.Count == 0) return null;
            return rng.PickWeighted(_sim.Jobs, job =>
            {
                if (excluded != null && excluded(job)) return 0f;
                if (job.Id == "student") return profile.Age <= 25 ? 3f : 0f;
                float weight = 1f;
                if (tendencies.Hangouts.Contains(job.Location)) weight += 1.5f;
                LocationEntry location = _sim.Location(job.Location);
                if (location != null && location.Tags.Any(tag => profile.Interest(tag) >= LikeThreshold)) weight += 1.5f;
                return weight;
            })?.Id;
        }

        /// <summary>Places that host something they love first, then their archetype's usual spots. At most four.</summary>
        List<string> Hangouts(ResidentProfile profile, ArchetypeTendencies tendencies)
        {
            var hangouts = new List<string>();
            foreach (KeyValuePair<string, float> interest in profile.Interests.Where(i => i.Value >= LikeThreshold).OrderByDescending(i => i.Value))
                foreach (LocationEntry location in _sim.Locations)
                    if (location.Tags.Contains(interest.Key) && !hangouts.Contains(location.Id)) hangouts.Add(location.Id);
            foreach (string location in tendencies.Hangouts)
                if (!hangouts.Contains(location)) hangouts.Add(location);
            if (hangouts.Count == 0) hangouts.AddRange(_sim.Locations.Where(l => l.Tags.Contains("social")).Select(l => l.Id));
            return hangouts.Take(4).ToList();
        }

        /// <summary>
        /// What they find appealing on others: mostly their own style, plus a soft spot for one other archetype
        /// (opposites do attract). Scaled so the favourite tag is 1.
        /// </summary>
        SortedDictionary<string, float> StyleTaste(string ownStyle, Pcg32 rng)
        {
            var taste = new SortedDictionary<string, float>(StringComparer.Ordinal);
            void Add(StyleArchetype archetype, float scale)
            {
                if (archetype == null) return;
                foreach (KeyValuePair<string, float> tag in archetype.TagWeights)
                {
                    taste.TryGetValue(tag.Key, out float current);
                    taste[tag.Key] = current + tag.Value * scale;
                }
            }

            Add(_characters.Archetype(ownStyle), 1f);
            List<StyleArchetype> others = _characters.Archetypes.Where(a => a.Id != ownStyle).ToList();
            if (others.Count > 0) Add(rng.Pick(others), 0.6f);

            float max = taste.Count == 0 ? 0f : taste.Values.Max();
            if (max > 0f)
                foreach (string tag in taste.Keys.ToList()) taste[tag] /= max;
            return taste;
        }
    }
}
