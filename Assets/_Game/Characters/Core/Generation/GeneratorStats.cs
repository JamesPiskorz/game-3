using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Game.Characters.Content;
using Game.Characters.Dna;

namespace Game.Characters.Generation
{
    /// <summary>
    /// The generator's headless batch statistics (PLAN.md Phase 2): a large batch of characters, measured for what
    /// they look like as a population. It counts styles, presentation, skin tones, hair and eye colours, how often
    /// each part is worn and in which contexts, patterns, rule violations and time per character. Content that's never
    /// worn, or worn by nearly everyone, shows up here before anyone has to notice it in a lineup.
    /// </summary>
    public sealed class GeneratorStats
    {
        public int Count;
        public double MillisecondsPerCharacter;
        public int Violations;
        public List<string> ViolationExamples = new List<string>();

        public SortedDictionary<string, int> Styles = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public int[] Femininity = new int[10];
        public int[] SkinTone = new int[10];
        public int FantasyHair, FantasyEyes, Heterochromia, GradientHair, Lipstick;

        /// <summary>Part ID → how many characters wear it, per outfit context (hair counts once per character).</summary>
        public SortedDictionary<string, SortedDictionary<string, int>> PartUse = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        public SortedDictionary<string, int> PatternUse = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public int PatternedItems, Items;

        public static GeneratorStats Measure(CharacterGenerator generator, int count, ulong firstSeed = 1)
        {
            ContentCatalog catalog = generator.Catalog;
            var stats = new GeneratorStats { Count = count };
            List<PaletteColour> naturalHair = catalog.Palette(catalog.Hair.NaturalPalette)?.Colours ?? new List<PaletteColour>();
            List<PaletteColour> naturalEyes = catalog.Palette(catalog.Eyes.NaturalPalette)?.Colours ?? new List<PaletteColour>();
            bool Natural(List<PaletteColour> palette, HsvColor colour) => palette.Any(c => HsvColor.Distance(HsvColor.FromHex(c.Hex), colour) < 0.12f);
            void Use(string part, string context)
            {
                if (!stats.PartUse.TryGetValue(part, out SortedDictionary<string, int> byContext))
                    stats.PartUse[part] = byContext = new SortedDictionary<string, int>(StringComparer.Ordinal);
                byContext[context] = byContext.TryGetValue(context, out int n) ? n + 1 : 1;
            }

            var timer = Stopwatch.StartNew();
            var characters = new List<CharacterDna>(count);
            for (int i = 0; i < count; i++) characters.Add(generator.Generate(firstSeed + (ulong)i * 7919UL));
            timer.Stop();
            stats.MillisecondsPerCharacter = timer.Elapsed.TotalMilliseconds / Math.Max(1, count);

            foreach (CharacterDna dna in characters)
            {
                List<string> errors = DnaValidator.Validate(catalog, dna);
                if (errors.Count > 0)
                {
                    stats.Violations++;
                    if (stats.ViolationExamples.Count < 5) stats.ViolationExamples.Add($"seed {dna.Seed}: {errors[0]}");
                }
                stats.Styles[dna.Style ?? "?"] = stats.Styles.TryGetValue(dna.Style ?? "?", out int s) ? s + 1 : 1;
                stats.Femininity[Bucket(dna.Body.Femininity)]++;
                stats.SkinTone[Bucket(dna.Skin.Tone)]++;
                if (!Natural(naturalHair, dna.Hair.Base)) stats.FantasyHair++;
                if (dna.Hair.Gradient > 0f) stats.GradientHair++;
                if (!Natural(naturalEyes, dna.Face.IrisLeft)) stats.FantasyEyes++;
                if (HsvColor.Distance(dna.Face.IrisLeft, dna.Face.IrisRight) > 1e-4f) stats.Heterochromia++;
                if (dna.Face.Makeup.Any(m => m.OverlayId == Makeup.Lipstick)) stats.Lipstick++;
                foreach (string piece in dna.Hair.PieceIds) Use(piece, "hair");
                foreach (OverlayDna mark in dna.Face.Makeup)
                    if (catalog.Part(mark.OverlayId) != null) Use(mark.OverlayId, "hair"); // worn whatever the outfit, like hair
                foreach (OutfitDna outfit in dna.Outfits)
                    foreach (ItemDna item in outfit.Items)
                    {
                        Use(item.ItemId, outfit.Context);
                        stats.Items++;
                        if (item.PatternId == null) continue;
                        stats.PatternedItems++;
                        stats.PatternUse[item.PatternId] = stats.PatternUse.TryGetValue(item.PatternId, out int p) ? p + 1 : 1;
                    }
            }
            return stats;
        }

        /// <summary>How many characters wear a part in any context (or at all, for hair).</summary>
        public int Worn(string partId) => PartUse.TryGetValue(partId, out SortedDictionary<string, int> byContext) ? byContext.Values.Max() : 0;

        static int Bucket(float value) => Math.Max(0, Math.Min(9, (int)(value * 10f)));

        public string ToMarkdown(ContentCatalog catalog, string header = null)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            string Pct(int n) => (100.0 * n / Math.Max(1, Count)).ToString("0.0", inv) + "%";
            var md = new StringBuilder();
            md.AppendLine("# Generator statistics").AppendLine();
            if (header != null) md.AppendLine(header).AppendLine();
            md.AppendLine($"{Count:N0} characters, {MillisecondsPerCharacter.ToString("0.000", inv)} ms each to generate. " +
                          $"Rule violations: **{Violations}**" + (Violations == 0 ? "." : ": " + string.Join("; ", ViolationExamples)));
            md.AppendLine();

            md.AppendLine("## Styles").AppendLine().AppendLine("| Style | Share | Catalog weight |").AppendLine("|---|---|---|");
            float totalWeight = catalog.Archetypes.Sum(a => a.Weight);
            foreach (StyleArchetype a in catalog.Archetypes)
                md.AppendLine($"| {a.Name ?? a.Id} | {Pct(Styles.TryGetValue(a.Id, out int n) ? n : 0)} | {(100.0 * a.Weight / totalWeight).ToString("0.0", inv)}% |");
            md.AppendLine();

            md.AppendLine("## Presentation and skin").AppendLine();
            md.AppendLine("Share of characters in each tenth of the range (0 = most masculine / lightest).").AppendLine();
            md.AppendLine("| | " + string.Join(" | ", Enumerable.Range(0, 10).Select(i => $"{i / 10.0:0.0}")) + " |");
            md.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", 10)));
            md.AppendLine("| Femininity | " + string.Join(" | ", Femininity.Select(Pct)) + " |");
            md.AppendLine("| Skin tone | " + string.Join(" | ", SkinTone.Select(Pct)) + " |");
            md.AppendLine();
            md.AppendLine($"Fantasy hair {Pct(FantasyHair)}, gradient tips {Pct(GradientHair)}, fantasy eyes {Pct(FantasyEyes)}, heterochromia {Pct(Heterochromia)}, lipstick {Pct(Lipstick)}.");
            md.AppendLine();

            md.AppendLine("## Parts").AppendLine();
            md.AppendLine("How many characters wear each part, per outfit context (hair: per character). **Never** marks content nobody wears.").AppendLine();
            List<string> contexts = new[] { "hair" }.Concat(catalog.Contexts.Select(c => c.Id)).ToList();
            md.AppendLine("| Part | Slot | " + string.Join(" | ", contexts) + " |");
            md.AppendLine("|---|---|" + string.Concat(Enumerable.Repeat("---|", contexts.Count)));
            foreach (PartEntry part in catalog.Parts.OrderBy(p => p.Slot, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal))
            {
                PartUse.TryGetValue(part.Id, out SortedDictionary<string, int> byContext);
                string Cell(string context)
                {
                    bool hair = part.Slot.StartsWith("hair", StringComparison.Ordinal) || Makeup.IsMark(part.Slot);
                    if (hair != (context == "hair")) return "";
                    int n = byContext != null && byContext.TryGetValue(context, out int v) ? v : 0;
                    return n == 0 ? "never" : Pct(n);
                }
                string name = Worn(part.Id) == 0 ? $"**{part.Id}**" : part.Id;
                md.AppendLine($"| {name} | {part.Slot} | " + string.Join(" | ", contexts.Select(Cell)) + " |");
            }
            md.AppendLine();

            md.AppendLine("## Patterns").AppendLine();
            md.AppendLine($"{(100.0 * PatternedItems / Math.Max(1, Items)).ToString("0.0", inv)}% of worn items are patterned.").AppendLine();
            md.AppendLine("| Pattern | Items |").AppendLine("|---|---|");
            foreach (PatternEntry pattern in catalog.Patterns)
                md.AppendLine($"| {pattern.Name ?? pattern.Id} | {(PatternUse.TryGetValue(pattern.Id, out int n) ? n : 0)} |");
            return md.ToString();
        }
    }
}
