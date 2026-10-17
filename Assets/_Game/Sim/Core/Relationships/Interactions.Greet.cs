using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Rng;
using Game.Sim.Residents;
using Game.Sim.Social;

namespace Game.Sim.Relationships
{
    /// <summary>Saying hello: first impressions, what they remember, news and gossip, jealousy, and noticing new looks.</summary>
    public sealed partial class Interactions
    {
        /// <summary>
        /// Hello. Meeting someone while wearing something they love gets it noticed. Coming back on a later day, they
        /// bring up the last good moment you shared: a great date, a gift they loved, a topic they lit up about.
        /// </summary>
        public InteractionResult Greet(Resident resident)
        {
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.Greet);
            if (result.FirstMeeting)
            {
                // Family talks: a brother or sister who's fond of the player has told them all about you.
                Resident fan = _world.SiblingsOf(resident).FirstOrDefault(s => _world.Find(s.Index)?.Met == true && _world.Find(s.Index).Affection >= GoodWordAffection);
                if (fan != null && rel.HeardFrom.Add(fan.Index))
                {
                    Add(ref rel.Affection, 3f);
                    Add(ref rel.Familiarity, 2f);
                    return Finish(resident, rel, rng, result, before, "greet.stranger.family", GreetMinutes,
                        ("sibling", fan.Profile.FirstName), ("relation", fan.Profile.SiblingWord));
                }
                PartEntry favourite = Favourite(resident);
                if (favourite != null && Impression(resident) >= ImpressedAt)
                    return Finish(resident, rel, rng, result, before, "greet.stranger.impressed", GreetMinutes, ("item", ItemName(favourite)));
                return Finish(resident, rel, rng, result, before, "greet.stranger", GreetMinutes);
            }

            // Someone they're seeing has heard about the player's date with someone else.
            if (rel.Dating && rel.HeardAboutDatesWith.Count > 0)
            {
                string other = _world.Residents[rel.HeardAboutDatesWith.First()].Profile.FirstName;
                rel.HeardAboutDatesWith.Clear();
                rel.Hurt++;
                Add(ref rel.Affection, rel.Partner ? -10f : -5f);
                Add(ref rel.Trust, rel.Partner ? -12f : -6f);
                Add(ref rel.Attraction, rel.Partner ? -3f : -1f);
                bool leaving = rel.Partner ? rel.Hurt >= 2 || rel.Trust < BreakUpTrust : rel.Hurt >= 3;
                if (leaving)
                {
                    rel.Partner = false;
                    rel.Dating = false;
                    result.Learnt = $"{resident.Profile.FirstName} has ended things with you.";
                    return Finish(resident, rel, rng, result, before, "greet.breakup", GreetMinutes, ("other", other));
                }
                result.Learnt = $"{resident.Profile.FirstName} knows about your date with {other}.";
                return Finish(resident, rel, rng, result, before, "greet.cheated", GreetMinutes, ("other", other));
            }

            if (_previousSeen >= 0 && _previousSeen < Today)
            {
                // The player's new hair, if they knew the old: how it lands depends on their taste.
                Makeover mine = _world.Player.LastMakeover;
                if (mine != null && _previousSeen < mine.Day && rel.NoticedPlayerMakeoverDay < mine.Day && Today - mine.Day <= Makeovers.NewForDays)
                {
                    rel.NoticedPlayerMakeoverDay = mine.Day;
                    float taste = FirstImpression.HairTaste(resident.Profile, _world.Player.Dna, _world.Characters);
                    string verdict = taste >= LovesHairAt ? "love" : taste < MehHairBelow ? "meh" : "nice";
                    if (verdict == "love")
                    {
                        Add(ref rel.Attraction, 4f);
                        Add(ref rel.Affection, 1f);
                        result.Learnt = $"{resident.Profile.FirstName} loves your new look.";
                    }
                    else if (verdict == "nice") Add(ref rel.Attraction, 1.5f);
                    else Add(ref rel.Attraction, -1f);
                    return Finish(resident, rel, rng, result, before, "greet.player.makeover." + verdict, GreetMinutes);
                }

                // A new look since the player last saw them: they bring it up (and a compliment now counts double).
                Makeover change = Makeovers.Noticeable(_world, resident, rel, _previousSeen);
                if (change != null && rel.MentionedMakeoverDay < change.Day)
                {
                    rel.MentionedMakeoverDay = change.Day;
                    return Finish(resident, rel, rng, result, before, "greet.makeover." + Makeovers.Key(change.Kind), GreetMinutes);
                }

                (string Key, (string, string) Token)? recalled = Recall(rel);
                if (recalled != null) return Finish(resident, rel, rng, result, before, recalled.Value.Key, GreetMinutes, recalled.Value.Token);

                // Jealousy: the player is dating them and also dating one of their friends (or, worse, family).
                if (rel.Dating)
                    foreach (int friend in Circle(resident))
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
                    bool close = Circle(resident).Contains(e.A) || Circle(resident).Contains(e.B) || e.A == resident.Index || e.B == resident.Index;
                    int id = _world.Social.Events.ToList().IndexOf(e);
                    if (!close || !rel.HeardNews.Add(id)) continue;
                    string a = _world.Residents[e.A].Profile.FirstName, b = _world.Residents[e.B].Profile.FirstName;
                    bool self = e.A == resident.Index || e.B == resident.Index;
                    string other = e.A == resident.Index ? b : a;
                    string key = e.Kind == SocialEventKind.Together ? (self ? "greet.news.mine.together" : "greet.news.together")
                        : (self ? "greet.news.mine.apart" : "greet.news.apart");
                    return Finish(resident, rel, rng, result, before, key, GreetMinutes, ("a", a), ("b", b), ("other", other));
                }

                // Word gets around: a friend (or sibling) of theirs who's fond of the player has put in a good word.
                foreach (int friend in Circle(resident))
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

        /// <summary>How much they must like a style for new hair to win them over, or how little for it to fall flat.</summary>
        public const float LovesHairAt = 0.4f, MehHairBelow = 0.2f;
    }
}
