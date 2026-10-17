using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Rng;
using Game.Sim.Residents;

namespace Game.Sim.Relationships
{
    public enum InteractionKind
    {
        Greet,
        Talk,
        Compliment,
        Gift,
        AskOnDate,
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
    public sealed class Interactions
    {
        public const int GreetMinutes = 5;
        public const int TalkMinutes = 15;
        public const int ComplimentMinutes = 5;
        public const int GiftMinutes = 10;
        public const int DateMinutes = 120;

        /// <summary>Each talk the same day counts for less: the second 2/3, the third 1/2. Visiting daily beats camping.</summary>
        static float Freshness(int talksToday) => 1f / (1f + 0.5f * talksToday);

        /// <summary>Days before asking again after a no.</summary>
        public const int RefusalCooldownDays = 3;

        /// <summary>Affection and attraction, weighted 60:40, a resident needs to accept a first date.</summary>
        public const float DateThreshold = 55f;

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

        /// <summary>
        /// Hello. Meeting someone while wearing something they love gets it noticed. Coming back on a later day, they
        /// bring up the last good moment you shared: a great date, a gift they loved, a topic they lit up about.
        /// </summary>
        public InteractionResult Greet(Resident resident)
        {
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.Greet);
            if (result.FirstMeeting)
            {
                PartEntry favourite = Favourite(resident);
                if (favourite != null && Impression(resident) >= ImpressedAt)
                    return Finish(resident, rel, rng, result, before, "greet.stranger.impressed", GreetMinutes, ("item", ItemName(favourite)));
                return Finish(resident, rel, rng, result, before, "greet.stranger", GreetMinutes);
            }

            if (_previousSeen >= 0 && _previousSeen < Today)
            {
                (string Key, (string, string) Token)? recalled = Recall(rel);
                if (recalled != null) return Finish(resident, rel, rng, result, before, recalled.Value.Key, GreetMinutes, recalled.Value.Token);
            }

            string greeting = rel.Stage switch
            {
                Stage.Stranger => "greet.acquaintance",
                Stage.Acquaintance => "greet.acquaintance",
                Stage.Friend => "greet.friend",
                Stage.Close => "greet.close",
                _ => "greet.dating",
            };
            return Finish(resident, rel, rng, result, before, greeting, GreetMinutes);
        }

        /// <summary>The most recent memory from the last few days, which is then used up.</summary>
        (string Key, (string, string) Token)? Recall(Relationship rel)
        {
            Memory latest = new[] { rel.GreatDate, rel.LovedGift, rel.LovedTopic }
                .Where(m => m != null && m.Day < Today && Today - m.Day <= MemoryDays)
                .OrderByDescending(m => m.Day)
                .FirstOrDefault();
            if (latest == null) return null;

            if (latest == rel.GreatDate)
            {
                rel.GreatDate = null;
                return ("greet.remember.date", ("location", _world.Catalog.Location(latest.Id)?.Name ?? latest.Id));
            }
            if (latest == rel.LovedGift)
            {
                rel.LovedGift = null;
                return ("greet.remember.gift", ("gift", _world.Catalog.Gift(latest.Id)?.Name ?? latest.Id));
            }
            rel.LovedTopic = null;
            return ("greet.remember.topic", ("topic", _world.Catalog.Topic(latest.Id)?.Name ?? latest.Id));
        }

        PartEntry Favourite(Resident resident) => _world.Player.Dna == null ? null
            : FirstImpression.Favourite(resident.Profile, _world.Player.Dna, _world.Player.Outfit, _world.Characters);

        static string ItemName(PartEntry part) => (part.Name ?? ContentCatalog.Humanize(part.Id)).ToLowerInvariant();

        public InteractionResult Talk(Resident resident, string topicId)
        {
            TopicEntry topic = _world.Catalog.Topic(topicId) ?? throw new ArgumentException($"Unknown topic '{topicId}'.", nameof(topicId));
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.Talk);
            ResidentProfile profile = resident.Profile;

            float sociable = 0.75f + 0.5f * profile.Personality.Extraversion;
            float fresh = Freshness(rel.TalksToday);
            float interest = profile.Interest(topicId);
            string key;
            if (rel.TopicDays.TryGetValue(topicId, out int day) && day == Today)
            {
                key = "talk.bored";
                Add(ref rel.Affection, -1f);
                Add(ref rel.Familiarity, 0.5f);
            }
            else if (interest >= ProfileGenerator.LikeThreshold)
            {
                key = "talk.love";
                rel.LovedTopic = new Memory { Id = topicId, Day = Today };
                Add(ref rel.Affection, (3f + 2f * rng.NextFloat()) * fresh);
                Add(ref rel.Familiarity, 3f * sociable * fresh);
                Add(ref rel.Attraction, 0.5f * fresh);
                if (rel.KnownLikes.Add(topicId)) result.Learnt = $"{profile.FirstName} loves {topic.Name}.";
            }
            else if (interest <= -ProfileGenerator.LikeThreshold)
            {
                key = "talk.dislike";
                Add(ref rel.Affection, -3f);
                Add(ref rel.Familiarity, 1f * sociable);
                if (rel.KnownDislikes.Add(topicId)) result.Learnt = $"{profile.FirstName} isn't into {topic.Name}.";
            }
            else
            {
                key = "talk.neutral";
                Add(ref rel.Affection, 1f * fresh);
                Add(ref rel.Familiarity, 2f * sociable * fresh);
            }
            rel.TalksToday++;
            rel.TopicDays[topicId] = Today;
            return Finish(resident, rel, rng, result, before, key, TalkMinutes, ("topic", topic.Name));
        }

        public InteractionResult Compliment(Resident resident)
        {
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.Compliment);
            Personality p = resident.Profile.Personality;
            string key;
            if (rel.LastComplimentDay == Today)
            {
                key = "compliment.awkward";
                Add(ref rel.Affection, -2f);
            }
            else if (before.Stage == Stage.Stranger && (p.Neuroticism > 0.6f || p.Agreeableness < 0.4f))
            {
                key = "compliment.awkward";
                Add(ref rel.Affection, -1f);
            }
            else
            {
                key = "compliment.pleased";
                float look = Math.Max(0f, Impression(resident));
                Add(ref rel.Affection, 2f);
                Add(ref rel.Attraction, 2f + 3f * look);

                // Dressed to their taste, they return the compliment.
                PartEntry favourite = Favourite(resident);
                if (favourite != null && look >= ImpressedAt)
                {
                    rel.LastComplimentDay = Today;
                    return Finish(resident, rel, rng, result, before, "compliment.style", ComplimentMinutes, ("item", ItemName(favourite)));
                }
            }
            rel.LastComplimentDay = Today;
            return Finish(resident, rel, rng, result, before, key, ComplimentMinutes);
        }

        public InteractionResult Gift(Resident resident, string giftId)
        {
            GiftEntry gift = _world.Catalog.Gift(giftId) ?? throw new ArgumentException($"Unknown gift '{giftId}'.", nameof(giftId));
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.Gift);
            ResidentProfile profile = resident.Profile;

            string key;
            if (rel.LastGiftDay == Today)
            {
                key = "gift.again";
                Add(ref rel.Affection, 1f);
            }
            else if (before.Stage == Stage.Stranger)
            {
                key = "gift.awkward";
                Add(ref rel.Affection, -1f);
                Add(ref rel.Familiarity, 2f);
            }
            else
            {
                float best = gift.Topics.Count == 0 ? 0f : gift.Topics.Max(t => profile.Interest(t));
                float worst = gift.Topics.Count == 0 ? 0f : gift.Topics.Min(t => profile.Interest(t));
                if (best >= ProfileGenerator.LikeThreshold)
                {
                    key = "gift.love";
                    rel.LovedGift = new Memory { Id = giftId, Day = Today };
                    Add(ref rel.Affection, 6f);
                    Add(ref rel.Trust, 2f);
                    Add(ref rel.Familiarity, 2f);
                    string topic = gift.Topics.First(t => profile.Interest(t) >= ProfileGenerator.LikeThreshold);
                    if (rel.KnownLikes.Add(topic)) result.Learnt = $"{profile.FirstName} loves {_world.Catalog.Topic(topic).Name}.";
                }
                else if (worst <= -ProfileGenerator.LikeThreshold)
                {
                    key = "gift.dislike";
                    Add(ref rel.Affection, -5f);
                    Add(ref rel.Familiarity, 1f);
                    string topic = gift.Topics.First(t => profile.Interest(t) <= -ProfileGenerator.LikeThreshold);
                    if (rel.KnownDislikes.Add(topic)) result.Learnt = $"{profile.FirstName} isn't into {_world.Catalog.Topic(topic).Name}.";
                }
                else
                {
                    key = "gift.ok";
                    Add(ref rel.Affection, 2f);
                    Add(ref rel.Familiarity, 2f);
                }
            }
            rel.LastGiftDay = Today;
            return Finish(resident, rel, rng, result, before, key, GiftMinutes, ("gift", gift.Name));
        }

        /// <summary>
        /// Friends and up can ask. A yes means the date happens now, at <paramref name="locationId"/>; how it goes
        /// depends on whether the place suits them. Two great dates out of three, with enough affection and trust,
        /// makes them a partner.
        /// </summary>
        public InteractionResult AskOnDate(Resident resident, string locationId)
        {
            LocationEntry location = _world.Catalog.Location(locationId) ?? throw new ArgumentException($"Unknown location '{locationId}'.", nameof(locationId));
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.AskOnDate);
            ResidentProfile profile = resident.Profile;
            var place = ("location", location.Name);

            if (before.Stage < Stage.Friend || Today - rel.LastRefusalDay < RefusalCooldownDays)
            {
                if (before.Stage < Stage.Friend) Add(ref rel.Affection, -1f);
                return Finish(resident, rel, rng, result, before, "date.notyet", GreetMinutes, place);
            }

            float willing = rel.Affection * 0.6f + rel.Attraction * 0.4f + rng.NextNormal() * 4f;
            if (!rel.Dating && willing < DateThreshold)
            {
                Add(ref rel.Affection, -3f);
                rel.LastRefusalDay = Today;
                return Finish(resident, rel, rng, result, before, "date.no", GreetMinutes, place);
            }

            float suits = location.Tags.Sum(t => profile.Interest(t)) + (location.Tags.Contains("romantic") ? 0.3f : 0f) + rng.NextNormal() * 0.15f;
            bool great = suits >= 0.5f;
            if (great)
            {
                Add(ref rel.Affection, 8f);
                Add(ref rel.Attraction, 6f);
                Add(ref rel.Trust, 5f);
                Add(ref rel.Familiarity, 6f);
                rel.GreatDates++;
                rel.GreatDate = new Memory { Id = locationId, Day = Today };
            }
            else
            {
                Add(ref rel.Affection, 4f);
                Add(ref rel.Attraction, 2f);
                Add(ref rel.Trust, 3f);
                Add(ref rel.Familiarity, 5f);
            }
            rel.Dates++;
            rel.Dating = true;
            if (rel.Dates >= 3 && rel.GreatDates >= 2 && rel.Affection >= 80f && rel.Trust >= 60f) rel.Partner = true;

            result.FollowUpReaction = great ? "date.great" : "date.fine";
            result.FollowUp = Say(resident, rel, rng, result.FollowUpReaction, before.Stage, Tokens(resident, place));
            return Finish(resident, rel, rng, result, before, "date.yes", DateMinutes, place);
        }

        /// <summary>How the player's current outfit strikes this resident, -1..1.</summary>
        public float Impression(Resident resident) => _world.Player.Dna == null ? 0f :
            FirstImpression.Score(resident.Profile, _world.Player.Dna, _world.Player.Outfit, _world.Characters);

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
            var before = new Meters(rel);

            if (!rel.Met)
            {
                rel.Met = true;
                result.FirstMeeting = true;
                float impression = Impression(resident);
                rel.Attraction = Clamp(20f + 25f * impression + rng.NextNormal() * 3f);
                rel.Affection = Clamp(5f + 5f * impression);
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
