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
            OutfitContext context = catalog.Context(outfit.Context);
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

                // Made in another of the fabrics it can be, at times: a skirt in denim, corduroy or tweed, by style and
                // by occasion (velvet and taffeta at a gala, flannel and waffle in bed).
                if (part.Fabrics.Count > 1 && rng.Chance(FabricVariety))
                {
                    List<FabricEntry> options = part.Fabrics.Select(catalog.Fabric).Where(f => f != null).ToList();
                    FabricEntry fabric = options.Count == 0 ? null
                        : rng.PickWeighted(options, f => f.Weight * Affinity.Of(archetype, f.Tags) * Affinity.Of(context, f.Tags));
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
                // A graphic (a tee's chest print, a back print) is its own choice, not just one print among many: a
                // casual top that can take one has one about GraphicChance of the time, on top of other prints.
                List<PatternEntry> graphics = prints.Where(p => p.Placement == "chest" || p.Placement == "back").ToList();
                bool graphic = graphics.Count > 0 && rng.Chance(GraphicChance);
                if (graphic) prints = graphics;
                if (prints.Count > 0 && (graphic || rng.Chance(archetype.PatternChance * (part.Slot == Slots.Shoes ? ShoePatternShare : 1f))))
                {
                    // First what kind of print: one placed once (a graphic tee, a badge, a dye, a border) about as
                    // often as PlacedPrintShare, whatever their number, so the many graphics don't each vanish among
                    // the repeats; then which, by style and by occasion: clouds and bunnies on pyjamas, tropical leaves
                    // on swimwear, damask at a gala.
                    List<PatternEntry> placed = prints.Where(p => p.Placement != null).ToList();
                    List<PatternEntry> repeats = prints.Where(p => p.Placement == null).ToList();
                    List<PatternEntry> kind = placed.Count > 0 && (repeats.Count == 0 || rng.Chance(PlacedPrintShare)) ? placed : repeats;
                    PatternEntry pattern = rng.PickWeighted(kind, p => p.Weight * Affinity.Of(archetype, p.Tags) * Affinity.Of(context, p.Tags));
                    if (pattern != null)
                    {
                        // In the trim, unless the item took its fabric's own colour and that matches the trim.
                        HsvColor worn = item.RegionColors.Count > 0 ? item.RegionColors[0] : main;
                        HsvColor ink = HsvColor.Distance(trim, worn) >= 0.15f ? trim : Colours.Shift(worn, 0f, 0f, worn.V > 0.5f ? -0.35f : 0.35f);
                        // Or tone on tone, at times: a deeper or paler shade of the cloth's own colour (a smart floral,
                        // a self-coloured check), more often for elegant styles.
                        if (rng.Chance(TonalPrintChance * (archetype.TagWeights.TryGetValue("elegant", out float elegant) ? 1f + elegant : 1f)))
                            ink = Colours.Shift(worn, 0f, 0.05f, worn.V > 0.45f ? -0.22f : 0.22f);
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
            MatchSet(rng, catalog, outfit);
        }

        /// <summary>
        /// Pyjamas, bikinis and lingerie mostly, and now and then a casual co-ord, are a matching set: the top's colour,
        /// and its print where the bottom takes it, carried on to the bottom. Casual co-ords only when the top is
        /// printed; pyjamas, swimwear and lingerie plain or printed.
        /// </summary>
        static void MatchSet(Pcg32 rng, ContentCatalog catalog, OutfitDna outfit)
        {
            ItemDna top = outfit.Items.FirstOrDefault(i => catalog.Part(i.ItemId)?.Slot == Slots.Top);
            ItemDna bottom = outfit.Items.FirstOrDefault(i => catalog.Part(i.ItemId)?.Slot == Slots.Bottom);
            if (top == null || bottom == null || top.RegionColors.Count == 0 || bottom.RegionColors.Count == 0) return;
            bool sets = outfit.Context == "sleep" || outfit.Context == "swim";
            if (!sets && top.PatternId == null) return;
            if (!rng.Chance(sets ? SleepSetChance : CoordChance)) return;
            if (top.PatternId != null && !catalog.TakesPrint(catalog.Part(bottom.ItemId), bottom.FabricId, top.PatternId))
            {
                if (!sets) return;
                // A plain bottom in the top's colour still makes a set.
            }
            else
            {
                bottom.PatternId = top.PatternId;
                bottom.PatternScale = top.PatternScale;
                bottom.PatternColor = top.PatternColor;
            }
            bottom.RegionColors[0] = top.RegionColors[0];
        }

        /// <summary>How often pyjamas, swimwear and lingerie are a matching set, and other outfits with a printed top a co-ord.</summary>
        public const float SleepSetChance = 0.7f;
        public const float CoordChance = 0.08f;

        /// <summary>How often a casual top or dress that can take a graphic (a chest or back print) wears one.</summary>
        public const float GraphicChance = 0.12f;

        /// <summary>How often a printed item that can take a placed print (a graphic, a badge, a dye, a border) gets one.</summary>
        public const float PlacedPrintShare = 0.3f;

        /// <summary>How much less often than other items shoes are printed.</summary>
        public const float ShoePatternShare = 0.4f;

        /// <summary>How often a print is tone on tone (a shade of the cloth's own colour), before style.</summary>
        public const float TonalPrintChance = 0.12f;

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
