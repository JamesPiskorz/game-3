using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Rng;

namespace Game.Characters.Generation
{
    /// <summary>
    /// Colours an outfit from the archetype's palette with a harmony scheme rather than independent random picks:
    /// one accent plus neutrals, placed by a scheme (accent top, accent bottom, monochrome, or neutral with accent
    /// details), with top and bottom kept apart in value or hue so outfits read as separate pieces.
    /// </summary>
    public static class OutfitColourer
    {
        public const float MinTopBottomContrast = 0.15f;

        enum Scheme { AccentTop, AccentBottom, Monochrome, AccentDetails }

        public static void Colour(Pcg32 rng, ContentCatalog catalog, StyleArchetype archetype, ColourPalette palette, IReadOnlyList<PartEntry> parts, OutfitDna outfit)
        {
            List<PaletteColour> neutrals = palette.Colours.Where(c => c.Neutral).ToList();
            List<PaletteColour> accents = palette.Colours.Where(c => !c.Neutral).ToList();
            if (neutrals.Count == 0) neutrals = palette.Colours;
            if (accents.Count == 0) accents = palette.Colours;

            HsvColor accent = Colours.Pick(rng, accents);
            HsvColor neutral1 = Colours.Pick(rng, neutrals);
            HsvColor neutral2 = Colours.Pick(rng, neutrals);
            for (int retry = 0; retry < 3 && HsvColor.Distance(neutral1, neutral2) < 0.1f && neutrals.Count > 1; retry++)
                neutral2 = Colours.Pick(rng, neutrals);

            var scheme = (Scheme)PickIndex(rng, 0.4f, 0.25f, 0.2f, 0.15f);
            HsvColor top, bottom;
            switch (scheme)
            {
                case Scheme.AccentTop: top = accent; bottom = neutral1; break;
                case Scheme.AccentBottom: top = neutral1; bottom = accent; break;
                case Scheme.Monochrome: top = accent; bottom = Colours.Shift(accent, 0f, 0.05f, accent.V > 0.5f ? -0.35f : 0.3f); break;
                default: top = neutral1; bottom = neutral2; break;
            }
            if (HsvColor.Distance(top, bottom) < MinTopBottomContrast)
                bottom = Colours.Shift(bottom, 0f, 0f, bottom.V > 0.5f ? -0.35f : 0.35f);

            foreach (PartEntry part in parts)
            {
                HsvColor main;
                switch (part.Slot)
                {
                    case Slots.Top:
                    case Slots.OnePiece: main = top; break;
                    case Slots.Bottom: main = bottom; break;
                    case Slots.Shoes: main = scheme == Scheme.AccentDetails ? accent : rng.Chance(0.5f) ? neutral1 : neutral2; break;
                    case Slots.Hat: main = scheme == Scheme.AccentTop ? neutral2 : accent; break;
                    default: main = neutral2; break;
                }

                // Trim contrasts with the main colour: the accent on neutral pieces, a neutral on accented ones.
                HsvColor trim = HsvColor.Distance(main, accent) > 0.2f ? accent : HsvColor.Distance(main, neutral1) > 0.2f ? neutral1 : neutral2;
                if (HsvColor.Distance(main, trim) < 0.12f)
                    trim = Colours.Shift(main, 0f, 0f, main.V > 0.5f ? -0.3f : 0.3f);

                var item = new ItemDna { ItemId = part.Id };
                HsvColor[] regions = { main, trim, neutral2, accent };
                for (int region = 0; region < part.Regions && region < regions.Length; region++)
                    item.RegionColors.Add(Colours.Jitter(rng, regions[region], 0.006f, 0.04f, 0.04f));

                if (part.Patternable && rng.Chance(archetype.PatternChance))
                {
                    PatternEntry pattern = rng.PickWeighted(catalog.Patterns, p => p.Weight * Affinity.Of(archetype, p.Tags));
                    if (pattern != null)
                    {
                        item.PatternId = pattern.Id;
                        item.PatternScale = rng.Range(pattern.MinScale, pattern.MaxScale);
                        item.PatternColor = Colours.Jitter(rng, trim, 0.006f, 0.04f, 0.04f);
                    }
                }
                outfit.Items.Add(item);
            }
        }

        static int PickIndex(Pcg32 rng, params float[] weights)
        {
            var indices = Enumerable.Range(0, weights.Length).ToList();
            return rng.PickWeighted(indices, i => weights[i]);
        }
    }
}
