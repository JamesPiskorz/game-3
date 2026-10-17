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
        DateRound,
        DateEnd,
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
    public sealed class Interactions
    {
        public const int GreetMinutes = 5;
        public const int TalkMinutes = 15;
        public const int ComplimentMinutes = 5;
        public const int GiftMinutes = 10;
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

                // Jealousy: the player is dating them and also dating one of their friends.
                if (rel.Dating)
                    foreach (int friend in _world.Social.FriendsOf(resident.Index))
                    {
                        Relationship rival = _world.Find(friend);
                        if (rival == null || !rival.Dating || !rel.JealousOf.Add(friend)) continue;
                        Add(ref rel.Affection, -JealousyAffection);
                        Add(ref rel.Trust, -JealousyTrust);
                        return Finish(resident, rel, rng, result, before, "greet.jealous", GreetMinutes, ("friend", _world.Residents[friend].Profile.FirstName));
                    }

                // News: a change among their friends (or themselves) in the last few days.
                foreach (SocialEvent e in _world.Social.Events.Reverse())
                {
                    if (Today - e.Day > MemoryDays) break;
                    if (e.Kind == SocialEventKind.LeftForPlayer) continue;
                    bool close = e.A == resident.Index || e.B == resident.Index
                        || _world.Social.AreFriends(resident.Index, e.A) || _world.Social.AreFriends(resident.Index, e.B);
                    int id = _world.Social.Events.ToList().IndexOf(e);
                    if (!close || !rel.HeardNews.Add(id)) continue;
                    string a = _world.Residents[e.A].Profile.FirstName, b = _world.Residents[e.B].Profile.FirstName;
                    bool self = e.A == resident.Index || e.B == resident.Index;
                    string other = e.A == resident.Index ? b : a;
                    string key = e.Kind == SocialEventKind.Together ? (self ? "greet.news.mine.together" : "greet.news.together")
                        : (self ? "greet.news.mine.apart" : "greet.news.apart");
                    return Finish(resident, rel, rng, result, before, key, GreetMinutes, ("a", a), ("b", b), ("other", other));
                }

                // Word gets around: a friend of theirs who's fond of the player has put in a good word.
                foreach (int friend in _world.Social.FriendsOf(resident.Index))
                {
                    Relationship theirs = _world.Find(friend);
                    if (theirs == null || !theirs.Met || theirs.Affection < GoodWordAffection || !rel.HeardFrom.Add(friend)) continue;
                    Add(ref rel.Affection, 2f);
                    Add(ref rel.Familiarity, 2f);
                    return Finish(resident, rel, rng, result, before, "greet.gossip", GreetMinutes, ("friend", _world.Residents[friend].Profile.FirstName));
                }
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
                // Knowing your stuff about something they love counts for more.
                float expertise = StatFor(topicId) == null ? 0f : _world.Player.Stat(StatFor(topicId)) / 100f;
                Add(ref rel.Affection, (3f + 2f * rng.NextFloat()) * fresh * (1f + 0.5f * expertise));
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
                float charm = _world.Player.Stat("charm") / 100f;
                Add(ref rel.Affection, 2f * (1f + charm));
                Add(ref rel.Attraction, (2f + 3f * look) * (1f + 0.5f * charm));

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
        /// Friends and up can ask. A yes starts a date at <paramref name="locationId"/> right away, returned in
        /// <paramref name="scene"/> (null on a no) to be played with <see cref="DateChoose"/> and
        /// <see cref="FinishDate"/>.
        /// </summary>
        public InteractionResult Invite(Resident resident, string locationId, out DateScene scene)
        {
            scene = null;
            LocationEntry location = _world.Catalog.Location(locationId) ?? throw new ArgumentException($"Unknown location '{locationId}'.", nameof(locationId));
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.AskOnDate);
            var place = ("location", location.Name);

            if (before.Stage < Stage.Friend || Today - rel.LastRefusalDay < RefusalCooldownDays)
            {
                if (before.Stage < Stage.Friend) Add(ref rel.Affection, -1f);
                return Finish(resident, rel, rng, result, before, "date.notyet", GreetMinutes, place);
            }

            // Someone already seeing a resident turns the player down, unless the player is who they really want.
            int partner = _world.Social.PartnerOf(resident.Index);
            string answer = "date.yes";
            if (partner >= 0 && !rel.Dating)
            {
                var partnerName = ("partner", _world.Residents[partner].Profile.FirstName);
                if (rel.Affection < LeaveForYouAffection || rel.Attraction < LeaveForYouAttraction)
                    return Finish(resident, rel, rng, result, before, "date.taken", GreetMinutes, place, partnerName);

                _world.Social.LeaveForPlayer(resident.Index, Today);
                Relationship ex = _world.Find(partner);
                if (ex != null && ex.Met) ex.Affection = Clamp(ex.Affection - ExResentment);
                result.Learnt = $"{resident.Profile.FirstName} is leaving {partnerName.Item2} for you.";
                answer = "date.leaves";
                scene = new DateScene { With = resident, Place = location };
                scene.Options = DateOptions(scene, rng);
                return Finish(resident, rel, rng, result, before, answer, DateStartMinutes, place, partnerName);
            }

            float willing = rel.Affection * 0.6f + rel.Attraction * 0.4f + rng.NextNormal() * 4f;
            if (!rel.Dating && willing < DateThreshold)
            {
                Add(ref rel.Affection, -3f);
                rel.LastRefusalDay = Today;
                return Finish(resident, rel, rng, result, before, "date.no", GreetMinutes, place);
            }

            scene = new DateScene { With = resident, Place = location };
            scene.Options = DateOptions(scene, rng);
            return Finish(resident, rel, rng, result, before, "date.yes", DateStartMinutes, place);
        }

        /// <summary>
        /// The player turns up where a resident invited them: no asking needed, the date starts. Returned in
        /// <paramref name="scene"/>, like <see cref="Invite"/>'s yes.
        /// </summary>
        public InteractionResult MeetUp(Resident resident, string locationId, out DateScene scene)
        {
            LocationEntry location = _world.Catalog.Location(locationId) ?? throw new ArgumentException($"Unknown location '{locationId}'.", nameof(locationId));
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.AskOnDate);
            Add(ref rel.Trust, 2f);
            scene = new DateScene { With = resident, Place = location };
            scene.Options = DateOptions(scene, rng);
            return Finish(resident, rel, rng, result, before, "invite.met", 0, ("location", location.Name));
        }

        /// <summary>
        /// Asks someone out and, on a yes, plays the whole date choosing as an attentive player would. The result is
        /// the answer, with how the date went as its follow-up and every meter change of the evening.
        /// </summary>
        public InteractionResult AskOnDate(Resident resident, string locationId)
        {
            Relationship rel = _world.RelationshipWith(resident);
            var before = new Meters(rel);
            InteractionResult answer = Invite(resident, locationId, out DateScene scene);
            if (scene == null) return answer;

            while (!scene.Done) DateChoose(scene, scene.BestOption());
            InteractionResult outcome = FinishDate(scene);
            answer.FollowUp = outcome.Line;
            answer.FollowUpReaction = outcome.Reaction;
            answer.Learnt ??= outcome.Learnt;
            answer.Familiarity = rel.Familiarity - before.Familiarity;
            answer.Affection = rel.Affection - before.Affection;
            answer.Attraction = rel.Attraction - before.Attraction;
            answer.Trust = rel.Trust - before.Trust;
            answer.After = rel.Stage;
            return answer;
        }

        /// <summary>Plays one round of a date: the player picks <paramref name="option"/> from the scene's options.</summary>
        public InteractionResult DateChoose(DateScene scene, int option)
        {
            if (scene.Done || scene.Finished) throw new InvalidOperationException("That date has no rounds left.");
            DateOption choice = scene.Options[option];
            Resident resident = scene.With;
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.DateRound);
            ResidentProfile profile = resident.Profile;

            string key;
            float score;
            var tokens = new List<(string, string)> { ("location", scene.Place.Name) };
            if (choice.Kind == DateOptionKind.Compliment)
            {
                key = scene.Complimented ? "date.compliment.again" : "date.compliment";
                score = scene.Complimented ? -0.2f : 0.6f;
                if (!scene.Complimented) Add(ref rel.Attraction, 2f);
                scene.Complimented = true;
            }
            else
            {
                TopicEntry topic = _world.Catalog.Topic(choice.Topic);
                tokens.Add(("topic", topic.Name));
                float interest = profile.Interest(choice.Topic);
                if (scene.Talked.Contains(choice.Topic))
                {
                    key = "date.repeat";
                    score = -0.5f;
                }
                else if (interest >= ProfileGenerator.LikeThreshold)
                {
                    key = "date.talk.love";
                    score = 1f;
                    if (rel.KnownLikes.Add(choice.Topic)) result.Learnt = $"{profile.FirstName} loves {topic.Name}.";
                }
                else if (interest <= -ProfileGenerator.LikeThreshold)
                {
                    key = "date.talk.dislike";
                    score = -1f;
                    if (rel.KnownDislikes.Add(choice.Topic)) result.Learnt = $"{profile.FirstName} isn't into {topic.Name}.";
                }
                else
                {
                    key = "date.talk.neutral";
                    score = 0.3f;
                }
                scene.Talked.Add(choice.Topic);
            }

            scene.Score += score;
            scene.Round++;
            Add(ref rel.Familiarity, 1.5f);
            Add(ref rel.Affection, 2f * score);
            if (!scene.Done) scene.Options = DateOptions(scene, rng);
            return Finish(resident, rel, rng, result, before, key, DateRoundMinutes, tokens.ToArray());
        }

        /// <summary>
        /// Ends a date, early or after its last round, and works out how it went: the rounds' average plus how
        /// much the place suits them. A great date is remembered; three dates, two of them great, with enough
        /// affection and trust, make a partner.
        /// </summary>
        public InteractionResult FinishDate(DateScene scene)
        {
            if (scene.Finished) throw new InvalidOperationException("That date is already over.");
            Resident resident = scene.With;
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.DateEnd);
            LocationEntry location = scene.Place;

            float suits = location.Tags.Sum(t => resident.Profile.Interest(t)) + (location.Tags.Contains("romantic") ? 0.3f : 0f);
            float value = suits + scene.Score / DateScene.Rounds + rng.NextNormal() * 0.15f;
            string key;
            if (value >= GreatDateAt)
            {
                key = "date.great";
                Add(ref rel.Affection, 8f);
                Add(ref rel.Attraction, 6f);
                Add(ref rel.Trust, 5f);
                Add(ref rel.Familiarity, 6f);
                rel.GreatDates++;
                rel.GreatDate = new Memory { Id = location.Id, Day = Today };
            }
            else if (value >= FineDateAt)
            {
                key = "date.fine";
                Add(ref rel.Affection, 4f);
                Add(ref rel.Attraction, 2f);
                Add(ref rel.Trust, 3f);
                Add(ref rel.Familiarity, 5f);
            }
            else
            {
                key = "date.awkward";
                Add(ref rel.Affection, 1f);
                Add(ref rel.Attraction, -2f);
                Add(ref rel.Trust, 1f);
                Add(ref rel.Familiarity, 3f);
            }
            rel.Dates++;
            rel.Dating = true;
            if (rel.Dates >= 3 && rel.GreatDates >= 2 && rel.Affection >= 80f && rel.Trust >= 60f) rel.Partner = true;
            scene.Outcome = key;
            return Finish(resident, rel, rng, result, before, key, 0, ("location", location.Name));
        }

        /// <summary>
        /// Three topics not yet raised on this date (usually including one they love, so attention pays) and a
        /// compliment, in a shuffled order.
        /// </summary>
        List<DateOption> DateOptions(DateScene scene, Pcg32 rng)
        {
            ResidentProfile profile = scene.With.Profile;
            List<TopicEntry> fresh = _world.Catalog.Topics.Where(t => !scene.Talked.Contains(t.Id)).ToList();
            var picked = new List<TopicEntry>();
            void Take(IEnumerable<TopicEntry> from)
            {
                List<TopicEntry> left = from.Where(t => !picked.Contains(t)).ToList();
                if (left.Count > 0) picked.Add(rng.Pick(left));
            }

            if (rng.Chance(0.8f)) Take(fresh.Where(t => profile.Interest(t.Id) >= ProfileGenerator.LikeThreshold));
            Take(fresh.Where(t => Math.Abs(profile.Interest(t.Id)) < ProfileGenerator.LikeThreshold));
            while (picked.Count < 3 && fresh.Any(t => !picked.Contains(t))) Take(fresh);

            var options = picked.Select(t => new DateOption { Kind = DateOptionKind.Topic, Topic = t.Id, Label = "Talk about " + t.Name }).ToList();
            options.Add(new DateOption { Kind = DateOptionKind.Compliment, Label = "Pay a compliment" });
            for (int i = options.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                (options[i], options[j]) = (options[j], options[i]);
            }
            return options;
        }

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
