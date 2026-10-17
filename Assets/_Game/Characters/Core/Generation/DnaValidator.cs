using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;

namespace Game.Characters.Generation
{
    /// <summary>
    /// Checks a character against the catalog's rules: known IDs, safe slider ranges, valid colours, complete and
    /// conflict-free outfits. The generator must never produce a violation (the 10k-character sweep test), and the
    /// creator and save loader use it to catch hand-edited or out-of-date DNA.
    /// </summary>
    public static class DnaValidator
    {
        const float Epsilon = 1e-4f;

        public static List<string> Validate(ContentCatalog catalog, CharacterDna dna)
        {
            var errors = new List<string>();

            if (!InRange(dna.Body.Femininity, 0f, 1f)) errors.Add($"Femininity {dna.Body.Femininity} is outside 0..1.");
            foreach (SliderEntry slider in catalog.BodySliders)
            {
                if (!dna.Body.Sliders.TryGetValue(slider.Id, out float value)) errors.Add($"Body slider '{slider.Id}' is missing.");
                else if (!InRange(value, slider.Min, slider.Max)) errors.Add($"Body slider '{slider.Id}' = {value} is outside {slider.Min}..{slider.Max}.");
            }

            FacePreset preset = catalog.FacePresets.FirstOrDefault(p => p.Id == dna.Face.PresetId);
            if (preset == null) errors.Add($"Unknown face preset '{dna.Face.PresetId}'.");
            foreach (SliderEntry slider in catalog.FaceSliders)
            {
                float presetValue = 0f;
                preset?.Values.TryGetValue(slider.Id, out presetValue);
                if (!dna.Face.Sliders.TryGetValue(slider.Id, out float offset)) errors.Add($"Face slider '{slider.Id}' is missing.");
                else if (!InRange(presetValue + offset, slider.Min, slider.Max)) errors.Add($"Face slider '{slider.Id}' = {presetValue + offset} is outside {slider.Min}..{slider.Max}.");
            }
            CheckStyle(errors, catalog.EyeStyles, dna.Face.EyeStyleId, "eye style");
            CheckStyle(errors, catalog.BrowStyles, dna.Face.BrowStyleId, "brow style");
            CheckStyle(errors, catalog.LashStyles, dna.Face.LashStyleId, "lash style");
            CheckColour(errors, dna.Face.IrisLeft, "left iris");
            CheckColour(errors, dna.Face.IrisRight, "right iris");
            if (dna.Face.Makeup == null) errors.Add("The makeup list is missing.");
            else
            {
                var marks = new HashSet<string>();
                foreach (OverlayDna overlay in dna.Face.Makeup)
                {
                    string slot = Makeup.SlotOf(catalog, overlay?.OverlayId);
                    if (slot == null) errors.Add($"Unknown face mark or makeup '{overlay?.OverlayId}'.");
                    else if (!marks.Add(slot)) errors.Add($"Two face marks for '{slot}'.");
                    if (overlay != null) CheckColour(errors, overlay.Tint, $"'{overlay.OverlayId}' tint");
                }
            }

            if (!InRange(dna.Skin.Tone, 0f, 1f)) errors.Add($"Skin tone {dna.Skin.Tone} is outside 0..1.");
            if (!InRange(dna.Skin.Undertone, 0f, 1f)) errors.Add($"Undertone {dna.Skin.Undertone} is outside 0..1.");

            List<PartEntry> hair = Resolve(catalog, dna.Hair.PieceIds, errors, "hair piece");
            if (hair.Count(p => p.Slot == Slots.HairBack) != 1) errors.Add("Hair needs exactly one hair.back piece.");
            if (hair.Count(p => p.Slot == Slots.HairFront) > 1) errors.Add("Hair has more than one hair.front piece.");
            foreach (PartEntry piece in hair.Where(p => p.Slot != Slots.HairBack && p.Slot != Slots.HairFront))
                errors.Add($"'{piece.Id}' is not a hair piece.");
            CheckColour(errors, dna.Hair.Base, "hair base");
            CheckColour(errors, dna.Hair.Tip, "hair tip");
            CheckColour(errors, dna.Hair.Highlight, "hair highlight");
            if (!InRange(dna.Hair.Gradient, 0f, 1f)) errors.Add($"Hair gradient {dna.Hair.Gradient} is outside 0..1.");

            if (dna.Outfits.Count == 0) errors.Add("No outfits.");
            foreach (IGrouping<string, OutfitDna> duplicate in dna.Outfits.GroupBy(o => o.Context).Where(g => g.Count() > 1))
                errors.Add($"Two outfits for context '{duplicate.Key}'.");
            foreach (OutfitDna outfit in dna.Outfits)
                ValidateOutfit(catalog, outfit, hair, errors);

            return errors;
        }

        static void ValidateOutfit(ContentCatalog catalog, OutfitDna outfit, List<PartEntry> hair, List<string> errors)
        {
            OutfitContext context = catalog.Context(outfit.Context);
            if (context == null) errors.Add($"Unknown outfit context '{outfit.Context}'.");
            string where = $"outfit '{outfit.Context}'";

            List<PartEntry> items = Resolve(catalog, outfit.Items.Select(i => i.ItemId), errors, $"item in {where}");
            List<PartEntry> worn = hair.Concat(items).ToList();
            foreach (PartEntry part in items)
            {
                List<PartEntry> others = worn.Where(p => p != part).ToList();
                if (!CharacterGenerator.Compatible(part, others))
                    errors.Add($"'{part.Id}' conflicts with another part in {where} (slot, blocked slot or tag rule).");
                if (context != null && part.Contexts.Count > 0 && !part.Contexts.Contains(context.Id))
                    errors.Add($"'{part.Id}' doesn't belong in {where}.");
            }

            bool onePiece = items.Any(p => p.Slot == Slots.OnePiece);
            bool separates = items.Any(p => p.Slot == Slots.Top) && items.Any(p => p.Slot == Slots.Bottom);
            if (!onePiece && !separates) errors.Add($"{where} needs a top and bottom or a one-piece.");
            if (context != null && context.Shoes && items.All(p => p.Slot != Slots.Shoes)) errors.Add($"{where} needs shoes.");

            foreach (ItemDna item in outfit.Items)
            {
                PartEntry part = catalog.Part(item.ItemId);
                if (part == null) continue;
                if (item.RegionColors.Count != part.Regions)
                    errors.Add($"'{item.ItemId}' in {where} has {item.RegionColors.Count} colours; it uses {part.Regions}.");
                for (int i = 0; i < item.RegionColors.Count; i++)
                    CheckColour(errors, item.RegionColors[i], $"'{item.ItemId}' region {i + 1}");
                if (item.PatternId != null)
                {
                    if (!part.Patternable) errors.Add($"'{item.ItemId}' can't take a pattern.");
                    if (catalog.Patterns.All(p => p.Id != item.PatternId)) errors.Add($"Unknown pattern '{item.PatternId}'.");
                    if (item.PatternColor.HasValue) CheckColour(errors, item.PatternColor.Value, $"'{item.ItemId}' pattern");
                }
            }
        }

        static List<PartEntry> Resolve(ContentCatalog catalog, IEnumerable<string> ids, List<string> errors, string kind)
        {
            var parts = new List<PartEntry>();
            foreach (string id in ids)
            {
                PartEntry part = catalog.Part(id);
                if (part == null) errors.Add($"Unknown {kind} '{id}'.");
                else parts.Add(part);
            }
            return parts;
        }

        static void CheckStyle(List<string> errors, List<StyleEntry> styles, string id, string kind)
        {
            if (styles.Count > 0 && styles.All(s => s.Id != id)) errors.Add($"Unknown {kind} '{id}'.");
        }

        static void CheckColour(List<string> errors, HsvColor colour, string what)
        {
            if (!InRange(colour.H, 0f, 1f) || !InRange(colour.S, 0f, 1f) || !InRange(colour.V, 0f, 1f))
                errors.Add($"Colour of {what} {colour} is out of range.");
        }

        static bool InRange(float value, float min, float max) => !float.IsNaN(value) && value >= min - Epsilon && value <= max + Epsilon;
    }
}
