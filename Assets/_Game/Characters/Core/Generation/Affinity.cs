using System;
using System.Collections.Generic;
using Game.Characters.Content;

namespace Game.Characters.Generation
{
    /// <summary>Weight multipliers that steer picks towards what suits the character.</summary>
    public static class Affinity
    {
        // Floor for tagged items that share no tag with the archetype: rare, never impossible.
        const float Floor = 0.15f;

        /// <summary>Archetype fit: untagged items are neutral (1); tagged items earn the archetype's weights for their tags.</summary>
        public static float Of(StyleArchetype archetype, List<string> tags)
        {
            if (archetype == null || tags.Count == 0) return 1f;
            float sum = Floor;
            foreach (string tag in tags)
                if (archetype.TagWeights.TryGetValue(tag, out float weight)) sum += weight;
            return sum;
        }

        /// <summary>Context fit: a boost for tags the context favours (date → "date", "elegant").</summary>
        public static float Of(OutfitContext context, List<string> tags)
        {
            if (context == null) return 1f;
            float boost = 1f;
            foreach (string tag in tags)
                if (context.TagWeights.TryGetValue(tag, out float weight)) boost += weight;
            return boost;
        }

        /// <summary>
        /// Presentation fit: items leaning feminine (+1) or masculine (-1) are favoured on matching bodies, never
        /// ruled out on others (a 0.2 floor).
        /// </summary>
        public static float Of(float lean, float femininity) => Math.Max(0.2f, 1f + lean * (2f * femininity - 1f) * 0.8f);
    }
}
