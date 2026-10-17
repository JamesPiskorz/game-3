using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Rng;
using Game.Sim.Residents;

namespace Game.Sim.Social
{
    /// <summary>
    /// Who in town is friends with whom, and who's a couple: residents have lives that don't revolve around the
    /// player. Derived from the town's seed like everything else, from shared loves, shared haunts, temperament and
    /// how much each likes the other's style, so the same town always has the same friendships. Breakups are the
    /// only part that changes, and they're saved.
    /// </summary>
    public sealed class SocialWeb
    {
        public const int MaxFriends = 4;
        public const float FriendAffinity = 0.9f;

        /// <summary>About this share of residents start out in a couple.</summary>
        public const float CoupleShare = 0.25f;

        readonly Dictionary<int, SortedSet<int>> _friends = new Dictionary<int, SortedSet<int>>();
        readonly Dictionary<int, int> _partners = new Dictionary<int, int>();
        readonly SortedSet<int> _brokeUp = new SortedSet<int>();

        public IReadOnlyCollection<int> FriendsOf(int resident) =>
            _friends.TryGetValue(resident, out SortedSet<int> friends) ? friends : (IReadOnlyCollection<int>)Array.Empty<int>();

        /// <summary>The resident's partner, or -1 if single.</summary>
        public int PartnerOf(int resident) => _partners.TryGetValue(resident, out int partner) ? partner : -1;

        public bool AreFriends(int a, int b) => FriendsOf(a).Contains(b);
        public bool AreCouple(int a, int b) => PartnerOf(a) == b;

        /// <summary>Residents who left a partner (for the player); saved, so breakups stick.</summary>
        public IReadOnlyCollection<int> BrokeUp => _brokeUp;

        /// <summary>Ends <paramref name="resident"/>'s relationship; the two stay acquaintances, not friends.</summary>
        public void BreakUp(int resident)
        {
            int partner = PartnerOf(resident);
            if (partner < 0) return;
            _partners.Remove(resident);
            _partners.Remove(partner);
            _brokeUp.Add(resident);
        }

        public static SocialWeb Build(IReadOnlyList<Resident> residents, ulong worldSeed, IEnumerable<int> brokeUp = null)
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
                    pairs.Add((a, b, Spark(residents[a], residents[b], worldSeed) + 0.3f * affinity[a, b]));
            int couples = (int)(n * CoupleShare / 2f);
            foreach ((int a, int b, float _) in pairs.OrderByDescending(p => p.Spark).ThenBy(p => p.A).ThenBy(p => p.B))
            {
                if (couples == 0) break;
                if (web._partners.ContainsKey(a) || web._partners.ContainsKey(b)) continue;
                web._partners[a] = b;
                web._partners[b] = a;
                couples--;
            }

            if (brokeUp != null)
                foreach (int resident in brokeUp)
                    web.BreakUp(resident);
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
