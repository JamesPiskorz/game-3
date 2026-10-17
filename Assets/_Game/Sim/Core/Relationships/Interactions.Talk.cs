using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Rng;
using Game.Sim.Residents;
using Game.Sim.Social;

namespace Game.Sim.Relationships
{
    /// <summary>Talking about a topic, compliments and gifts.</summary>
    public sealed partial class Interactions
    {
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

                // Noticing a new look while it's new means a lot.
                Makeover change = Makeovers.Noticeable(_world, resident, rel, _previousSeen);
                if (change != null)
                {
                    rel.NoticedMakeoverDay = change.Day;
                    Add(ref rel.Affection, 4f * (1f + charm));
                    Add(ref rel.Attraction, 2f);
                    rel.LastComplimentDay = Today;
                    result.Learnt = $"{resident.Profile.FirstName} was hoping someone would notice {resident.Profile.Possessive} {Makeovers.Describe(change.Kind).Replace("a new ", "new ")}.";
                    return Finish(resident, rel, rng, result, before, "compliment.makeover." + Makeovers.Key(change.Kind), ComplimentMinutes);
                }

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
    }
}
