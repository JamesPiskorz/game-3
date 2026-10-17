using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Rng;
using Game.Sim.Residents;
using Game.Sim.Social;

namespace Game.Sim.Relationships
{
    public enum InteractionKind
    {
        Greet,
        Talk,
        Compliment,
        Gift,
        AskOnDate,
        Ask,
        Chat,
        DateRound,
        DateEnd,
        Confession,
    }

    /// <summary>What happened: the reaction, their line, and how the meters moved.</summary>
    public sealed class InteractionResult
    {
        public InteractionKind Kind;

        /// <summary>Dialogue key of the reaction ("talk.love", "date.no"...), which UIs can also key effects off.</summary>
        public string Reaction;
        public string Line;

        /// <summary>A second line where the interaction has an aftermath (how a date went), else null.</summary>
        public string FollowUp;
        public string FollowUpReaction;
        public Stage Before;
        public Stage After;
        public bool FirstMeeting;

        /// <summary>On a first meeting, how the player's look struck them (-1..1); else null.</summary>
        public float? Impression;
        public float Familiarity;
        public float Affection;
        public float Attraction;
        public float Trust;

        /// <summary>Something the player found out ("Mei loves music."), else null.</summary>
        public string Learnt;

        public bool StageChanged => Before != After;
    }

    /// <summary>
    /// The player's verbs. Every outcome is decided by the resident's profile and the relationship so far, with a
    /// little seeded noise, so the same save replays the same way. Each verb takes game time.
    /// </summary>
    public sealed partial class Interactions
    {
        public const int GreetMinutes = 5;
        public const int TalkMinutes = 15;
        public const int ComplimentMinutes = 5;
        public const int GiftMinutes = 10;
        public const int AskMinutes = 10;
        public const int DateMinutes = DateStartMinutes + DateScene.Rounds * DateRoundMinutes;
        public const int DateStartMinutes = 30;
        public const int DateRoundMinutes = 30;

        /// <summary>A date's value (place suitability plus the rounds' average) needed for a great or a fine date.</summary>
        public const float GreatDateAt = 0.9f;
        public const float FineDateAt = 0.2f;

        /// <summary>Each talk the same day counts for less: the second 2/3, the third 1/2. Visiting daily beats camping.</summary>
        static float Freshness(int talksToday) => 1f / (1f + 0.5f * talksToday);

        /// <summary>Days before asking again after a no.</summary>
        public const int RefusalCooldownDays = 3;

        /// <summary>Affection and attraction, weighted 60:40, a resident needs to accept a first date.</summary>
        public const float DateThreshold = 55f;

        /// <summary>What finding out the player is also dating their friend costs.</summary>
        public const float JealousyAffection = 6f;
        public const float JealousyTrust = 5f;

        /// <summary>How fond of the player a friend must be to put in a good word.</summary>
        public const float GoodWordAffection = 45f;

        /// <summary>How smitten someone in a couple must be to leave their partner for the player.</summary>
        public const float LeaveForYouAffection = 90f;
        public const float LeaveForYouAttraction = 80f;

        /// <summary>Affection an ex loses for the player who came between them.</summary>
        public const float ExResentment = 20f;

        /// <summary>How well an outfit has to land (impression, -1..1) for them to mention it.</summary>
        public const float ImpressedAt = 0.5f;

        /// <summary>How many days a shared moment stays fresh enough to bring up.</summary>
        public const int MemoryDays = 3;

        readonly SimWorld _world;
        readonly Dialogue _dialogue;
        int _previousSeen; // the day of the visit before this one, set by Begin

        public Interactions(SimWorld world)
        {
            _world = world;
            _dialogue = new Dialogue(world.Catalog);
        }

        int Today => _world.Now.Day;

        /// <summary>How the player's current outfit strikes this resident, -1..1.</summary>
        public float Impression(Resident resident)
        {
            float look = _world.Player.Dna == null ? 0f : FirstImpression.Score(resident.Profile, _world.Player.Dna, _world.Player.Outfit, _world.Characters);
            return Math.Min(1f, look + Admiration(resident));
        }

        /// <summary>
        /// How much the player's stats impress this resident, 0..0.35: their style admires certain qualities (sporty
        /// types admire fitness, bookish ones smarts), weighted by how strong the player is in them.
        /// </summary>
        public float Admiration(Resident resident)
        {
            _world.Catalog.Archetypes.TryGetValue(resident.Profile.Archetype ?? "", out ArchetypeTendencies tendencies);
            if (tendencies == null || tendencies.Admires.Count == 0) return 0f;
            float total = tendencies.Admires.Sum(a => a.Value * _world.Player.Stat(a.Key) / 100f);
            return 0.35f * total / tendencies.Admires.Values.Sum();
        }

        /// <summary>The stat that knowing a topic well depends on (sport: fitness, books: smarts, fashion: style), or null.</summary>
        static string StatFor(string topic) => topic switch
        {
            "sports" => "fitness",
            "books" => "smarts",
            "tech" => "smarts",
            "fashion" => "style",
            "art" => "style",
            _ => null,
        };

        readonly struct Meters
        {
            public readonly float Familiarity, Affection, Attraction, Trust;
            public readonly Stage Stage;

            public Meters(Relationship rel)
            {
                Familiarity = rel.Familiarity;
                Affection = rel.Affection;
                Attraction = rel.Attraction;
                Trust = rel.Trust;
                Stage = rel.Stage;
            }
        }

        /// <summary>
        /// Shared start of every verb: a seeded RNG for this action, the first meeting's impression, and the daily
        /// bookkeeping (talk fatigue resets; coming back on a new day builds trust).
        /// </summary>
        (Relationship, Pcg32, InteractionResult, Meters) Begin(Resident resident, InteractionKind kind)
        {
            Relationship rel = _world.RelationshipWith(resident);
            ulong seed = StableHash.SplitMix64(_world.Seed ^ StableHash.SplitMix64(resident.Dna.Seed ^ (ulong)_world.Actions));
            var rng = new Pcg32(seed, (ulong)kind);
            _world.Actions++;

            var result = new InteractionResult { Kind = kind, Before = rel.Stage };

            // A first meeting sets the starting spark from the player's look. That's reported as the impression,
            // not as meter changes, so the deltas show only what the interaction itself did.
            if (!rel.Met)
            {
                rel.Met = true;
                result.FirstMeeting = true;
                float impression = Impression(resident);
                result.Impression = impression;
                rel.Attraction = Clamp(20f + 25f * impression + rng.NextNormal() * 3f);
                rel.Affection = Clamp(5f + 5f * impression);
            }
            var before = new Meters(rel);

            if (result.FirstMeeting)
            {
                Add(ref rel.Familiarity, 4f);
            }
            else if (rel.LastSeenDay != Today)
            {
                float steadiness = 1f - 0.5f * resident.Profile.Personality.Neuroticism;
                Add(ref rel.Trust, 2f * steadiness);
                Add(ref rel.Familiarity, 1f);
            }
            _previousSeen = rel.LastSeenDay;
            if (rel.LastSeenDay != Today) rel.TalksToday = 0;
            rel.LastSeenDay = Today;
            return (rel, rng, result, before);
        }

        InteractionResult Finish(Resident resident, Relationship rel, Pcg32 rng, InteractionResult result, Meters before, string key, int minutes,
            params (string Key, string Value)[] extra)
        {
            result.Reaction = key;
            result.Line = Say(resident, rel, rng, key, before.Stage, Tokens(resident, extra));
            result.Familiarity = rel.Familiarity - before.Familiarity;
            result.Affection = rel.Affection - before.Affection;
            result.Attraction = rel.Attraction - before.Attraction;
            result.Trust = rel.Trust - before.Trust;
            result.After = rel.Stage;
            _world.Advance(minutes);
            return result;
        }

        /// <summary>A line for <paramref name="key"/>, never the same one as last time when there's a choice.</summary>
        string Say(Resident resident, Relationship rel, Pcg32 rng, string key, Stage stage, Dictionary<string, string> tokens)
        {
            rel.LastLines.TryGetValue(key, out string last);
            string template = _dialogue.Template(key, resident.Profile.Tone, stage, rng, last);
            rel.LastLines[key] = template;
            return Dialogue.Fill(template, tokens);
        }

        Dictionary<string, string> Tokens(Resident resident, params (string Key, string Value)[] extra)
        {
            var tokens = new Dictionary<string, string> { ["name"] = resident.Profile.FirstName, ["player"] = _world.Player.Name };
            foreach ((string key, string value) in extra) tokens[key] = value;
            return tokens;
        }

        static float Clamp(float value) => Math.Max(0f, Math.Min(100f, value));
        static void Add(ref float meter, float delta) => meter = Clamp(meter + delta);
    }
}
