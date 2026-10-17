using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Rng;
using Game.Sim.Residents;
using Game.Sim.Social;

namespace Game.Sim.Relationships
{
    /// <summary>Asking about what they love, their plans and their friends.</summary>
    public sealed partial class Interactions
    {
        public static readonly string[] Questions = { "likes", "plans", "friends" };

        /// <summary>The people whose news and opinions reach them: their siblings first, then their friends.</summary>
        IEnumerable<int> Circle(Resident resident) =>
            _world.SiblingsOf(resident).Select(s => s.Index).Concat(_world.Social.FriendsOf(resident.Index));

        /// <summary>
        /// Getting to know someone by asking: what they're into (reveals a love, or a dislike once those run out), what
        /// they're doing later (reveals where they'll be), or about their friends (who they're close to, and whether
        /// they're seeing anyone). Quicker than finding out by trial and error, but it builds less affection, the
        /// same question twice in a day gets an eye-roll, and strangers find personal questions nosy.
        /// </summary>
        public InteractionResult Ask(Resident resident, string question)
        {
            if (!Questions.Contains(question)) throw new ArgumentException($"Unknown question '{question}'.", nameof(question));
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.Ask);
            ResidentProfile profile = resident.Profile;
            if (rel.AskedDay != Today)
            {
                rel.AskedToday.Clear();
                rel.AskedDay = Today;
            }
            if (!rel.AskedToday.Add(question))
            {
                Add(ref rel.Affection, -1f);
                return Finish(resident, rel, rng, result, before, "ask.again", AskMinutes);
            }
            if (question != "likes" && before.Stage == Stage.Stranger)
            {
                Add(ref rel.Affection, -1f);
                Add(ref rel.Familiarity, 1f);
                return Finish(resident, rel, rng, result, before, "ask.nosy", AskMinutes);
            }

            Add(ref rel.Familiarity, 2f * (0.75f + 0.5f * profile.Personality.Extraversion));
            Add(ref rel.Affection, 0.5f);
            switch (question)
            {
                case "likes":
                {
                    string love = profile.Interests.Where(i => i.Value >= ProfileGenerator.LikeThreshold && !rel.KnownLikes.Contains(i.Key)).Select(i => i.Key).FirstOrDefault();
                    if (love != null)
                    {
                        rel.KnownLikes.Add(love);
                        string name = _world.Catalog.Topic(love).Name;
                        result.Learnt = $"{profile.FirstName} loves {name}.";
                        return Finish(resident, rel, rng, result, before, "ask.likes", AskMinutes, ("topic", name));
                    }
                    string hate = profile.Interests.Where(i => i.Value <= -ProfileGenerator.LikeThreshold && !rel.KnownDislikes.Contains(i.Key)).Select(i => i.Key).FirstOrDefault();
                    if (hate != null)
                    {
                        rel.KnownDislikes.Add(hate);
                        string name = _world.Catalog.Topic(hate).Name;
                        result.Learnt = $"{profile.FirstName} isn't into {name}.";
                        return Finish(resident, rel, rng, result, before, "ask.dislikes", AskMinutes, ("topic", name));
                    }
                    return Finish(resident, rel, rng, result, before, "ask.nothing", AskMinutes);
                }
                case "plans":
                {
                    string here = _world.WhereIs(resident);
                    for (int hour = _world.Now.Hour + 1; hour < 23; hour++)
                    {
                        string later = _world.WhereIs(resident, SimTime.At(Today, hour));
                        if (later == here || later == SimCatalog.Home) continue;
                        rel.Plan = new Memory { Id = later, Day = Today };
                        rel.PlanHour = hour;
                        string place = _world.Catalog.Location(later)?.Name ?? later;
                        result.Learnt = $"{profile.FirstName} will be at {place} from {hour}:00.";
                        return Finish(resident, rel, rng, result, before, "ask.plans", AskMinutes, ("location", place), ("time", $"{hour}:00"));
                    }
                    return Finish(resident, rel, rng, result, before, "ask.plans.none", AskMinutes);
                }
                default:
                {
                    rel.KnowsTheirLife = true;
                    List<Resident> siblings = _world.SiblingsOf(resident).ToList();
                    if (siblings.Count > 0)
                        result.Learnt = $"{profile.FirstName} is {string.Join(" and ", siblings.Select(s => s.Profile.FirstName))}'s {profile.SiblingWord}.";
                    var friends = _world.Social.FriendsOf(resident.Index).Select(i => _world.Residents[i].Profile.FirstName).ToList();
                    int partner = _world.Social.PartnerOf(resident.Index);
                    if (partner >= 0)
                        return Finish(resident, rel, rng, result, before, "ask.friends.partner", AskMinutes, ("partner", _world.Residents[partner].Profile.FirstName));
                    if (friends.Count > 0)
                        return Finish(resident, rel, rng, result, before, "ask.friends", AskMinutes, ("friend", friends[0]));
                    return Finish(resident, rel, rng, result, before, "ask.friends.none", AskMinutes);
                }
            }
        }
    }
}
