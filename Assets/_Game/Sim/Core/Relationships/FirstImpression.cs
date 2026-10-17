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

        public static float Score(ResidentProfile resident, CharacterDna player, string outfitContext, ContentCatalog characters)
        {
            OutfitDna outfit = player.Outfits.FirstOrDefault(o => o.Context == outfitContext) ?? player.Outfits.FirstOrDefault();
            if (outfit == null) return 0f;

            // Each styled item scores its best-liked tag; untagged basics don't count either way.
            float total = 0f;
            int styled = 0;
            foreach (ItemDna item in outfit.Items)
            {
                PartEntry part = characters.Part(item.ItemId);
                if (part == null) continue;
                float best = -1f;
                foreach (string tag in part.Tags)
                    if (characters.Archetypes.Any(a => a.TagWeights.ContainsKey(tag)))
                        best = Math.Max(best, resident.StyleTaste.TryGetValue(tag, out float taste) ? taste : 0f);
                if (best < 0f) continue;
                total += best;
                styled++;
            }
            if (styled == 0) return 0f;

            float style = total / styled;
            return Math.Max(-1f, Math.Min(1f, (style - Indifferent) / (1f - Indifferent)));
        }
    }
}
