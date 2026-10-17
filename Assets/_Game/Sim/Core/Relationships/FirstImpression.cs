using System;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Sim.Residents;

namespace Game.Sim.Relationships
{
    /// <summary>
    /// How the player's look lands with a resident they've just met, -1..1. This is where character creation feeds
    /// the sim: dressing in a style someone likes starts the spark higher. Clothing is the lever because the player
    /// can change it day to day, unlike their face.
    /// </summary>
    public static class FirstImpression
    {
        /// <summary>Taste below this reads as a poor match, above it as a good one.</summary>
        const float Indifferent = 0.3f;

        /// <summary>
        /// A face mark of the player's (freckles, a beauty mark, blush) whose style they like at least as much as a
        /// favourite piece of clothing needs, or null.
        /// </summary>
        public static PartEntry FavouriteMark(ResidentProfile resident, CharacterDna player, ContentCatalog characters)
        {
            PartEntry favourite = null;
            float best = 0.6f;
            foreach (OverlayDna mark in player.Face.Makeup ?? new System.Collections.Generic.List<OverlayDna>())
            {
                PartEntry part = mark?.OverlayId == null ? null : characters.Part(mark.OverlayId);
                if (part == null || !Makeup.IsMark(part.Slot)) continue;
                foreach (string tag in part.Tags)
                    if (resident.StyleTaste.TryGetValue(tag, out float taste) && taste >= best)
                    {
                        best = taste;
                        favourite = part;
                    }
            }
            return favourite;
        }

        /// <summary>What a face mark is called in conversation: "freckles", "beauty mark", "blush".</summary>
        public static string FeatureName(PartEntry mark) => mark.Slot switch
        {
            Slots.FaceFreckles => "freckles",
            Slots.FaceMole => "beauty mark",
            Slots.FaceBlush => "blush",
            _ => (mark.Name ?? mark.Id).ToLowerInvariant(),
        };

        /// <summary>
        /// What of the player's catches a stranger's eye: their favourite piece of clothing or face mark, whichever style
        /// they like more (the clothes on a tie), or null when nothing does. Nearly everyone who likes freckles likes some
        /// piece of clothing too, so without the comparison the face would never get noticed.
        /// </summary>
        public static PartEntry Noticed(ResidentProfile resident, CharacterDna player, string outfitContext, ContentCatalog characters)
        {
            PartEntry piece = Favourite(resident, player, outfitContext, characters);
            PartEntry mark = FavouriteMark(resident, player, characters);
            if (mark == null || piece == null) return piece ?? mark;
            return Taste(resident, mark) > Taste(resident, piece) ? mark : piece;
        }

        /// <summary>Their taste in a part's best-liked style tag (0 when they have none).</summary>
        static float Taste(ResidentProfile resident, PartEntry part) =>
            part.Tags.Select(tag => resident.StyleTaste.TryGetValue(tag, out float taste) ? taste : 0f).DefaultIfEmpty(0f).Max();

        /// <summary>The piece of the player's outfit this resident likes best, if they really like it (taste ≥ 0.6); else null.</summary>
        public static PartEntry Favourite(ResidentProfile resident, CharacterDna player, string outfitContext, ContentCatalog characters)
        {
            OutfitDna outfit = player.Outfits.FirstOrDefault(o => o.Context == outfitContext) ?? player.Outfits.FirstOrDefault();
            if (outfit == null) return null;
            PartEntry favourite = null;
            float best = 0.6f;
            foreach (ItemDna item in outfit.Items)
            {
                PartEntry part = characters.Part(item.ItemId);
                if (part == null) continue;
                foreach (string tag in part.Tags)
                    if (resident.StyleTaste.TryGetValue(tag, out float taste) && taste >= best)
                    {
                        best = taste;
                        favourite = part;
                    }
            }
            return favourite;
        }

        /// <summary>
        /// How much they like the player's hair, 0..1: their average taste across its style tags. An average, not the
        /// best tag, because nearly everyone likes something casual; what tells is whether the whole look is theirs.
        /// </summary>
        public static float HairTaste(ResidentProfile resident, CharacterDna player, ContentCatalog characters) =>
            player.Hair.PieceIds.Select(characters.Part).Where(p => p != null).SelectMany(p => p.Tags)
                .Where(tag => characters.Archetypes.Any(a => a.TagWeights.ContainsKey(tag))).Distinct()
                .Select(tag => resident.StyleTaste.TryGetValue(tag, out float taste) ? taste : 0f).DefaultIfEmpty(0f).Average();

        /// <summary>
        /// How much they like makeup on the player, 0..1: their taste in each style, weighted by how much that style
        /// wears lipstick and blush itself. Elegant, gothic and cute tastes like it; sporty ones less so.
        /// </summary>
        public static float MakeupTaste(ResidentProfile resident, ContentCatalog characters)
        {
            float total = 0f, weight = 0f;
            foreach (StyleArchetype style in characters.Archetypes)
            {
                if (!resident.StyleTaste.TryGetValue(style.Id, out float taste) || taste <= 0f) continue;
                style.FaceChances.TryGetValue(Makeup.Lipstick, out float lips);
                style.FaceChances.TryGetValue(Slots.FaceBlush, out float blush);
                total += taste * (lips + blush);
                weight += taste;
            }
            return weight > 0f ? total / weight : 0f;
        }

        public static float Score(ResidentProfile resident, CharacterDna player, string outfitContext, ContentCatalog characters)
        {
            OutfitDna outfit = player.Outfits.FirstOrDefault(o => o.Context == outfitContext) ?? player.Outfits.FirstOrDefault();
            if (outfit == null) return 0f;

            // Each styled item scores its best-liked tag; untagged basics don't count either way. Hair counts too.
            float total = 0f;
            int styled = 0;
            foreach (PartEntry part in outfit.Items.Select(i => characters.Part(i.ItemId)).Concat(player.Hair.PieceIds.Select(characters.Part)))
            {
                if (part == null) continue;
                float best = -1f;
                foreach (string tag in part.Tags)
                    if (characters.Archetypes.Any(a => a.TagWeights.ContainsKey(tag)))
                        best = Math.Max(best, resident.StyleTaste.TryGetValue(tag, out float taste) ? taste : 0f);
                if (best < 0f) continue;
                total += best;
                styled++;
            }
            // Makeup is part of the look too (freckles and beauty marks aren't a choice, so they don't count).
            if (player.Face.Makeup != null && player.Face.Makeup.Any(m => m.OverlayId == Makeup.Lipstick || Makeup.SlotOf(characters, m.OverlayId) == Slots.FaceBlush))
            {
                total += MakeupTaste(resident, characters);
                styled++;
            }
            if (styled == 0) return 0f;

            float style = total / styled;
            return Math.Max(-1f, Math.Min(1f, (style - Indifferent) / (1f - Indifferent)));
        }
    }
}
