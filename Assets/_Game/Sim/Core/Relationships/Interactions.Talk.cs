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

                // Dressed to their taste (or with freckles, a beauty mark or blush they like even more), they return the compliment.
                PartEntry noticed = _world.Player.Dna == null ? null
                    : FirstImpression.Noticed(resident.Profile, _world.Player.Dna, _world.Player.Outfit, _world.Characters);
                if (noticed != null && look >= ImpressedAt)
                {
                    rel.LastComplimentDay = Today;
                    return Makeup.IsMark(noticed.Slot)
                        ? Finish(resident, rel, rng, result, before, "compliment.style.mark", ComplimentMinutes, ("feature", FirstImpression.FeatureName(noticed)))
                        : Finish(resident, rel, rng, result, before, "compliment.style", ComplimentMinutes, ("item", ItemName(noticed)));
                }
            }
            rel.LastComplimentDay = Today;
            return Finish(resident, rel, rng, result, before, key, ComplimentMinutes);
        }

        /// <summary>
        /// How much they must like a piece of clothing (their average over its two best-liked style tags) to love it,
        /// or how little for it to miss. Over a town that measure runs from about 0.19 (20th percentile) to 0.77
        /// (80th), so roughly a fifth of gifts land each way.
        /// </summary>
        public const float LovesClothesAt = 0.75f, MehClothesBelow = 0.2f;

        /// <summary>How much a resident likes a piece: their taste for its two best-liked style tags, 0..1.</summary>
        public static float ClothesTaste(ResidentProfile profile, PartEntry part, Game.Characters.Content.ContentCatalog characters) =>
            part.Tags.Where(t => characters.Archetypes.Any(a => a.TagWeights.ContainsKey(t))).Distinct()
                .Select(t => profile.StyleTaste.TryGetValue(t, out float v) ? v : 0f).OrderByDescending(v => v).Take(2).DefaultIfEmpty(0f).Average();

        /// <summary>
        /// Gives the resident a piece of clothing. How it lands depends on their taste for its two best-liked style
        /// tags. A piece they love and can wear goes straight into their everyday outfit.
        /// </summary>
        public InteractionResult GiveClothes(Resident resident, Game.Characters.Dna.ItemDna item)
        {
            PartEntry part = _world.Characters.Part(item.ItemId) ?? throw new ArgumentException($"Unknown item '{item.ItemId}'.", nameof(item));
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.Gift);
            ResidentProfile profile = resident.Profile;
            string name = ItemName(part);
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
            }
            else
            {
                float taste = ClothesTaste(profile, part, _world.Characters);
                if (taste >= LovesClothesAt)
                {
                    key = "gift.clothes.love";
                    Add(ref rel.Affection, 6f);
                    Add(ref rel.Attraction, 2f);
                    Add(ref rel.Trust, 2f);
                    Game.Characters.Dna.CharacterDna dressed = ClothesGifts.Wearing(_world, resident, item);
                    if (dressed != null)
                    {
                        resident.Dna = dressed;
                        _world.GiftedClothes.Add(new GiftedClothes { Resident = resident.Index, Day = Today, Item = item });
                        result.Learnt = $"{profile.FirstName} is wearing your {name}.";
                    }
                    else result.Learnt = $"{profile.FirstName} loves the {name}, but it doesn't go with the rest of {profile.Possessive} look.";
                }
                else if (taste < MehClothesBelow)
                {
                    key = "gift.clothes.meh";
                    Add(ref rel.Affection, -1f);
                    result.Learnt = $"The {name} isn't really {profile.FirstName}'s style.";
                }
                else
                {
                    key = "gift.clothes.ok";
                    Add(ref rel.Affection, 2f);
                }
            }
            rel.LastGiftDay = Today;
            return Finish(resident, rel, rng, result, before, key, GiftMinutes, ("gift", "your " + name), ("item", name));
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
