using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Rng;
using Game.Sim.Residents;

namespace Game.Sim.Social
{
    public enum SocialEventKind
    {
        /// <summary>Two residents became a couple.</summary>
        Together,

        /// <summary>A couple split up.</summary>
        Apart,

        /// <summary>A resident left their partner for the player.</summary>
        LeftForPlayer,
    }

    /// <summary>A change in who's with whom, on a given day. The town's history is the seed plus these, in order.</summary>
    public sealed class SocialEvent
    {
        public int Day;
        public SocialEventKind Kind;
        public int A;
        public int B;
    }

    /// <summary>
    /// Who in town is friends with whom, and who's a couple: residents have lives that don't revolve around the
    /// player. The starting web comes from the town's seed (shared loves, shared haunts, temperament, how much each
    /// likes the other's style), so the same town always starts the same. Then it moves: each week some singles get
    /// together and a shaky couple may split, and the player can come between two people. Every change is a
    /// <see cref="SocialEvent"/>; saves keep the events and replay them.
    /// </summary>
    public sealed class SocialWeb
    {
        public const int MaxFriends = 4;
        public const float FriendAffinity = 0.9f;

        /// <summary>About this share of residents start out in a couple.</summary>
        public const float CoupleShare = 0.25f;

        readonly Dictionary<int, SortedSet<int>> _friends = new Dictionary<int, SortedSet<int>>();
        readonly Dictionary<int, int> _partners = new Dictionary<int, int>();
        readonly List<SocialEvent> _events = new List<SocialEvent>();
        readonly Dictionary<(int, int), float> _spark = new Dictionary<(int, int), float>();

        public IReadOnlyCollection<int> FriendsOf(int resident) =>
            _friends.TryGetValue(resident, out SortedSet<int> friends) ? friends : (IReadOnlyCollection<int>)Array.Empty<int>();

        /// <summary>The resident's partner, or -1 if single.</summary>
        public int PartnerOf(int resident) => _partners.TryGetValue(resident, out int partner) ? partner : -1;

        public bool AreFriends(int a, int b) => FriendsOf(a).Contains(b);
        public bool AreCouple(int a, int b) => PartnerOf(a) == b;

        /// <summary>Everything that's changed since the town began, oldest first.</summary>
        public IReadOnlyList<SocialEvent> Events => _events;

        /// <summary><paramref name="resident"/> leaves their partner for the player.</summary>
        public void LeaveForPlayer(int resident, int day)
        {
            int partner = PartnerOf(resident);
            if (partner >= 0) Apply(new SocialEvent { Day = day, Kind = SocialEventKind.LeftForPlayer, A = resident, B = partner });
        }

        public void Apply(SocialEvent e)
        {
            switch (e.Kind)
            {
                case SocialEventKind.Together:
                    _partners[e.A] = e.B;
                    _partners[e.B] = e.A;
                    break;
                default:
                    _partners.Remove(e.A);
                    _partners.Remove(e.B);
                    break;
            }
            _events.Add(e);
        }

        /// <summary>
        /// A week passes for everyone else: maybe one new couple among singles with a real spark, and maybe one
        /// shaky couple splits. Residents the player is dating are spoken for. Returns what happened.
        /// </summary>
        public List<SocialEvent> NewWeek(int day, ulong worldSeed, Func<int, bool> spokenFor)
        {
            var happened = new List<SocialEvent>();
            Pcg32 rng = SeedStreams.For(StableHash.SplitMix64(worldSeed ^ (ulong)day), "social.week");

            List<(int A, int B)> shaky = _partners.Where(p => p.Key < p.Value && SparkOf(p.Key, p.Value) < BreakUpSpark).Select(p => (p.Key, p.Value)).ToList();
            if (shaky.Count > 0 && rng.Chance(0.35f))
            {
                (int a, int b) = rng.Pick(shaky);
                happened.Add(new SocialEvent { Day = day, Kind = SocialEventKind.Apart, A = a, B = b });
            }

            List<(int A, int B)> sparks = _spark.Where(p => p.Value >= NewCoupleSpark && PartnerOf(p.Key.Item1) < 0 && PartnerOf(p.Key.Item2) < 0
                    && !spokenFor(p.Key.Item1) && !spokenFor(p.Key.Item2) && happened.All(h => h.A != p.Key.Item1 && h.B != p.Key.Item1 && h.A != p.Key.Item2 && h.B != p.Key.Item2))
                .Select(p => p.Key).OrderBy(p => p.Item1).ThenBy(p => p.Item2).ToList();
            if (sparks.Count > 0 && rng.Chance(0.5f))
            {
                (int a, int b) = rng.Pick(sparks);
                happened.Add(new SocialEvent { Day = day, Kind = SocialEventKind.Together, A = a, B = b });
            }

            foreach (SocialEvent e in happened) Apply(e);
            return happened;
        }

        /// <summary>Couples whose spark is below this may split; singles above <see cref="NewCoupleSpark"/> may get together.</summary>
        public const float BreakUpSpark = 1.1f;
        public const float NewCoupleSpark = 1.35f;

        float SparkOf(int a, int b) => _spark.TryGetValue((Math.Min(a, b), Math.Max(a, b)), out float spark) ? spark : 0f;

        public static SocialWeb Build(IReadOnlyList<Resident> residents, ulong worldSeed, IEnumerable<SocialEvent> history = null)
        {
            var web = new SocialWeb();
            int n = residents.Count;
            var affinity = new float[n, n];
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                    affinity[a, b] = affinity[b, a] = Affinity(residents[a], residents[b], worldSeed);

            // Friends: everyone's best matches above the bar, made mutual, a few each.
            for (int a = 0; a < n; a++)
            {
                IEnumerable<int> best = Enumerable.Range(0, n).Where(b => b != a && affinity[a, b] >= FriendAffinity)
                    .OrderByDescending(b => affinity[a, b]).ThenBy(b => b).Take(2);
                foreach (int b in best)
                    if (web.FriendsOf(a).Count < MaxFriends && web.FriendsOf(b).Count < MaxFriends)
                    {
                        web.Friend(a, b);
                        web.Friend(b, a);
                    }
            }

            // Couples: the strongest mutual sparks, until about a quarter of the town is paired up.
            var pairs = new List<(int A, int B, float Spark)>();
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                {
                    float spark = Spark(residents[a], residents[b], worldSeed) + 0.3f * affinity[a, b];
                    web._spark[(a, b)] = spark;
                    pairs.Add((a, b, spark));
                }
            int couples = (int)(n * CoupleShare / 2f);
            foreach ((int a, int b, float _) in pairs.OrderByDescending(p => p.Spark).ThenBy(p => p.A).ThenBy(p => p.B))
            {
                if (couples == 0) break;
                if (web._partners.ContainsKey(a) || web._partners.ContainsKey(b)) continue;
                web._partners[a] = b;
                web._partners[b] = a;
                couples--;
            }

            if (history != null)
                foreach (SocialEvent e in history)
                    web.Apply(e);
            return web;
        }

        void Friend(int a, int b)
        {
            if (!_friends.TryGetValue(a, out SortedSet<int> friends)) _friends[a] = friends = new SortedSet<int>();
            friends.Add(b);
        }

        /// <summary>How well two residents get on: shared loves and haunts, similar energy, and a little chemistry.</summary>
        public static float Affinity(Resident a, Resident b, ulong worldSeed)
        {
            ResidentProfile pa = a.Profile, pb = b.Profile;
            float score = 0f;
            foreach (KeyValuePair<string, float> interest in pa.Interests)
            {
                float other = pb.Interest(interest.Key);
                if (interest.Value >= ProfileGenerator.LikeThreshold && other >= ProfileGenerator.LikeThreshold) score += 0.6f;
                else if (interest.Value * other <= -0.3f) score -= 0.4f;
            }
            score += 0.25f * pa.Hangouts.Intersect(pb.Hangouts).Count();
            score += 0.5f * (1f - Math.Abs(pa.Personality.Extraversion - pb.Personality.Extraversion));
            score += 0.3f * (0.5f * (pa.Personality.Agreeableness + pb.Personality.Agreeableness) - 0.5f);
            return score + Noise(a, b, worldSeed, "affinity") * 0.3f;
        }

        /// <summary>Romantic chemistry: how much each likes the other's style, plus a little luck.</summary>
        public static float Spark(Resident a, Resident b, ulong worldSeed)
        {
            float Taste(ResidentProfile who, string style) => style != null && who.StyleTaste.TryGetValue(style, out float t) ? t : 0f;
            return Taste(a.Profile, b.Profile.Archetype) + Taste(b.Profile, a.Profile.Archetype) + Noise(a, b, worldSeed, "spark") * 0.4f;
        }

        /// <summary>-1..1, the same for (a, b) and (b, a).</summary>
        static float Noise(Resident a, Resident b, ulong worldSeed, string what)
        {
            ulong low = Math.Min(a.Dna.Seed, b.Dna.Seed), high = Math.Max(a.Dna.Seed, b.Dna.Seed);
            ulong hash = StableHash.SplitMix64(worldSeed ^ StableHash.SplitMix64(low) ^ StableHash.SplitMix64(high * 31UL) ^ StableHash.Fnv1a64(what));
            return (hash >> 40) / (float)(1UL << 24) * 2f - 1f;
        }
    }
}
