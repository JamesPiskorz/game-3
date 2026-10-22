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
    /// details), with top and bottom kept apart in value or hue so outfits read as separate pieces, and clothing over the
    /// torso and legs kept apart from the skin so it never reads as bare skin.
    /// </summary>
    public static class OutfitColourer
    {
        public const float MinTopBottomContrast = 0.15f;

        enum Scheme { AccentTop, AccentBottom, Monochrome, AccentDetails }

        public static void Colour(Pcg32 rng, ContentCatalog catalog, StyleArchetype archetype, ColourPalette palette, IReadOnlyList<PartEntry> parts, OutfitDna outfit, HsvColor skin)
        {
            float skinContrast = catalog.Skin.MinClothingContrast;
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
            top = AwayFromSkin(top, skin, skinContrast, accent, neutral1, neutral2);
            bottom = PickBottom(bottom, top, skin, skinContrast, neutral1, neutral2, accent);
            HsvColor wornTop = top, wornBottom = bottom;

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

                // Made in another of the fabrics it can be, at times: a skirt in denim, corduroy or tweed, by style.
                if (part.Fabrics.Count > 1 && rng.Chance(FabricVariety))
                {
                    List<FabricEntry> options = part.Fabrics.Select(catalog.Fabric).Where(f => f != null).ToList();
                    FabricEntry fabric = options.Count == 0 ? null : rng.PickWeighted(options, f => f.Weight * Affinity.Of(archetype, f.Tags));
                    if (fabric != null && fabric.Id != part.Fabrics[0]) item.FabricId = fabric.Id;
                }
                // Often in the fabric's own colour (indigo denim, natural straw, brown suede), where it still stands
                // apart from the skin and from the other half of the outfit.
                FabricEntry made = catalog.FabricOf(part, item.FabricId);
                if (made != null && made.Colours.Count > 0 && item.RegionColors.Count > 0 && rng.Chance(made.ColourChance))
                {
                    HsvColor natural = Colours.Pick(rng, made.Colours);
                    bool isTop = part.Slot == Slots.Top || part.Slot == Slots.OnePiece, isBottom = part.Slot == Slots.Bottom;
                    bool apart = (!isTop || HsvColor.Distance(natural, wornBottom) >= MinTopBottomContrast) &&
                                 (!isBottom || HsvColor.Distance(natural, wornTop) >= MinTopBottomContrast);
                    if (apart && HsvColor.Distance(natural, skin) >= skinContrast + JitterMargin)
                    {
                        item.RegionColors[0] = Colours.Jitter(rng, natural, 0.006f, 0.04f, 0.04f);
                        if (isTop) wornTop = natural;
                        if (isBottom) wornBottom = natural;
                    }
                }

                // A print, at times, of those its fabric takes (only knitted ones on a knit, animal ones on leather);
                // on shoes less often (leopard pumps are a statement).
                List<PatternEntry> prints = catalog.PrintsFor(part, item.FabricId);
                if (prints.Count > 0 && rng.Chance(archetype.PatternChance * (part.Slot == Slots.Shoes ? ShoePatternShare : 1f)))
                {
                    PatternEntry pattern = rng.PickWeighted(prints, p => p.Weight * Affinity.Of(archetype, p.Tags));
                    if (pattern != null)
                    {
                        // In the trim, unless the item took its fabric's own colour and that matches the trim.
                        HsvColor worn = item.RegionColors.Count > 0 ? item.RegionColors[0] : main;
                        HsvColor ink = HsvColor.Distance(trim, worn) >= 0.15f ? trim : Colours.Shift(worn, 0f, 0f, worn.V > 0.5f ? -0.35f : 0.35f);
                        // Or, now and then, in its classic colours (navy Breton stripes on white, leopard in tan and
                        // black), where that ground stands apart from the skin and the other half of the outfit.
                        if (pattern.Colourways.Count > 0 && item.RegionColors.Count > 0 && rng.Chance(pattern.ColourwayChance))
                        {
                            PrintColourway way = rng.Pick(pattern.Colourways);
                            HsvColor ground = HsvColor.FromHex(way.Base);
                            bool isTop = part.Slot == Slots.Top || part.Slot == Slots.OnePiece, isBottom = part.Slot == Slots.Bottom;
                            bool apart = (!isTop || HsvColor.Distance(ground, wornBottom) >= MinTopBottomContrast) &&
                                         (!isBottom || HsvColor.Distance(ground, wornTop) >= MinTopBottomContrast);
                            if (apart && HsvColor.Distance(ground, skin) >= skinContrast + JitterMargin)
                            {
                                item.RegionColors[0] = Colours.Jitter(rng, ground, 0.006f, 0.04f, 0.04f);
                                ink = HsvColor.FromHex(way.Print);
                                if (isTop) wornTop = ground;
                                if (isBottom) wornBottom = ground;
                            }
                        }
                        item.PatternId = pattern.Id;
                        item.PatternScale = rng.Range(pattern.MinScale, pattern.MaxScale);
                        item.PatternColor = Colours.Jitter(rng, ink, 0.006f, 0.04f, 0.04f);
                    }
                }
                outfit.Items.Add(item);
            }
        }

        /// <summary>How much less often than other items shoes are printed.</summary>
        public const float ShoePatternShare = 0.4f;

        /// <summary>How often an item that can be made in several fabrics isn't made in its own.</summary>
        public const float FabricVariety = 0.5f;

        // Margin for the per-item jitter applied afterwards, so the final colour still clears the minimum.
        const float JitterMargin = 0.08f;

        /// <summary>The colour if it stands apart from the skin; otherwise the first alternative that does; otherwise a value shift.</summary>
        static HsvColor AwayFromSkin(HsvColor colour, HsvColor skin, float minimum, params HsvColor[] alternatives)
        {
            float needed = minimum + JitterMargin;
            if (HsvColor.Distance(colour, skin) >= needed) return colour;
            foreach (HsvColor alternative in alternatives)
                if (HsvColor.Distance(alternative, skin) >= needed) return alternative;
            return Colours.Shift(colour, 0f, 0f, skin.V > 0.5f ? -0.45f : 0.45f);
        }

        /// <summary>
        /// The first candidate that stands apart from both the skin and the top: the scheme's choice, then the other
        /// palette picks, then lighter and darker variants. Skin contrast wins if nothing satisfies both.
        /// </summary>
        static HsvColor PickBottom(HsvColor bottom, HsvColor top, HsvColor skin, float skinContrast, HsvColor neutral1, HsvColor neutral2, HsvColor accent)
        {
            HsvColor[] candidates =
            {
                bottom, neutral1, neutral2, accent,
                Colours.Shift(bottom, 0f, 0f, -0.35f), Colours.Shift(bottom, 0f, 0f, 0.35f),
                Colours.Shift(top, 0f, 0f, -0.45f), Colours.Shift(top, 0f, 0f, 0.45f),
            };
            foreach (HsvColor candidate in candidates)
                if (HsvColor.Distance(candidate, skin) >= skinContrast + JitterMargin && HsvColor.Distance(candidate, top) >= MinTopBottomContrast)
                    return candidate;
            return AwayFromSkin(bottom, skin, skinContrast, neutral1, neutral2, accent);
        }

        static int PickIndex(Pcg32 rng, params float[] weights)
        {
            var indices = Enumerable.Range(0, weights.Length).ToList();
            return rng.PickWeighted(indices, i => weights[i]);
        }
    }
}
