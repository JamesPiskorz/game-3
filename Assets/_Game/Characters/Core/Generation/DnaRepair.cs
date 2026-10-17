using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;

namespace Game.Characters.Generation
{
    /// <summary>
    /// Brings a character made with other content up to date with this catalog, keeping as much of it as possible.
    /// Content changes all the time during development (a part is removed, a preset renamed, a slider or an outfit
    /// context added), and a saved look or share code shouldn't stop loading because of it. Anything unknown or out
    /// of range is replaced from a fresh generation of the same seed, in the same style and presentation, so the
    /// stand-in fits the person; ranges are clamped. If the result still isn't valid, the fresh character is used.
    /// </summary>
    public static class DnaRepair
    {
        public static CharacterDna Repair(CharacterGenerator generator, CharacterDna dna, out List<string> fixes)
        {
            ContentCatalog catalog = generator.Catalog;
            fixes = new List<string>();
            if (DnaValidator.Validate(catalog, dna).Count == 0) return dna;

            CharacterDna result = DnaSerializer.FromJson(DnaSerializer.ToJson(dna, indented: false));
            bool knownStyle = result.Style != null && catalog.Archetype(result.Style) != null;
            var request = new GenerationRequest
            {
                Archetype = knownStyle ? result.Style : null,
                FemininityMin = Clamp01(result.Body.Femininity),
                FemininityMax = Clamp01(result.Body.Femininity),
            };
            CharacterDna fresh = generator.Generate(result.Seed, request);
            if (!knownStyle)
            {
                fixes.Add($"style '{result.Style}' → '{fresh.Style}'");
                result.Style = fresh.Style;
            }
            result.Body.Femininity = Clamp01(result.Body.Femininity);

            // Sliders: fill in new ones, clamp the rest.
            foreach (SliderEntry slider in catalog.BodySliders)
            {
                if (!result.Body.Sliders.TryGetValue(slider.Id, out float value)) { result.Body.Sliders[slider.Id] = fresh.Body.Sliders[slider.Id]; fixes.Add($"new body slider '{slider.Id}'"); }
                else result.Body.Sliders[slider.Id] = Clamp(value, slider.Min, slider.Max);
            }
            foreach (string gone in result.Body.Sliders.Keys.Where(k => catalog.BodySliders.All(s => s.Id != k)).ToList()) result.Body.Sliders.Remove(gone);

            // Face: an unknown preset takes the fresh one's, keeping the offsets' absolute positions where they fit.
            FacePreset preset = catalog.FacePresets.FirstOrDefault(p => p.Id == result.Face.PresetId);
            if (preset == null)
            {
                fixes.Add($"face preset '{result.Face.PresetId}' → '{fresh.Face.PresetId}'");
                result.Face.PresetId = fresh.Face.PresetId;
                result.Face.Sliders = new SortedDictionary<string, float>(fresh.Face.Sliders, StringComparer.Ordinal);
                preset = catalog.FacePresets.First(p => p.Id == result.Face.PresetId);
            }
            foreach (SliderEntry slider in catalog.FaceSliders)
            {
                preset.Values.TryGetValue(slider.Id, out float basis);
                if (!result.Face.Sliders.TryGetValue(slider.Id, out float offset)) { result.Face.Sliders[slider.Id] = fresh.Face.Sliders[slider.Id]; fixes.Add($"new face slider '{slider.Id}'"); }
                else result.Face.Sliders[slider.Id] = Clamp(basis + offset, slider.Min, slider.Max) - basis;
            }
            foreach (string gone in result.Face.Sliders.Keys.Where(k => catalog.FaceSliders.All(s => s.Id != k)).ToList()) result.Face.Sliders.Remove(gone);
            result.Face.EyeStyleId = Known(catalog.EyeStyles, result.Face.EyeStyleId, fresh.Face.EyeStyleId, "eye style", fixes);
            result.Face.BrowStyleId = Known(catalog.BrowStyles, result.Face.BrowStyleId, fresh.Face.BrowStyleId, "brow style", fixes);
            result.Face.LashStyleId = Known(catalog.LashStyles, result.Face.LashStyleId, fresh.Face.LashStyleId, "lash style", fixes);
            result.Skin.Tone = Clamp01(result.Skin.Tone);
            result.Skin.Undertone = Clamp01(result.Skin.Undertone);
            result.Face.IrisLeft = Clamp(result.Face.IrisLeft);
            result.Face.IrisRight = Clamp(result.Face.IrisRight);

            // Face marks and makeup: unknown ones go, and so does a second one for the same slot.
            var markSlots = new HashSet<string>();
            var marks = new List<OverlayDna>();
            foreach (OverlayDna overlay in result.Face.Makeup ?? new List<OverlayDna>())
            {
                string slot = Makeup.SlotOf(catalog, overlay?.OverlayId);
                if (slot == null || !markSlots.Add(slot))
                {
                    fixes.Add($"face mark '{overlay?.OverlayId}' removed");
                    continue;
                }
                overlay.Tint = Clamp(overlay.Tint);
                marks.Add(overlay);
            }
            result.Face.Makeup = marks;
            result.Hair.Base = Clamp(result.Hair.Base);
            result.Hair.Tip = Clamp(result.Hair.Tip);
            result.Hair.Highlight = Clamp(result.Hair.Highlight);
            result.Hair.Gradient = Clamp01(result.Hair.Gradient);

            // Hair: unknown pieces go; without a back piece, the fresh hairstyle (in their own colours).
            int before = result.Hair.PieceIds.Count;
            result.Hair.PieceIds = result.Hair.PieceIds.Where(id => catalog.Part(id)?.Slot is string slot && (slot == Slots.HairBack || slot == Slots.HairFront)).ToList();
            if (result.Hair.PieceIds.Count < before) fixes.Add("hair pieces this build doesn't have");
            List<PartEntry> backs = result.Hair.PieceIds.Select(catalog.Part).Where(p => p.Slot == Slots.HairBack).ToList();
            if (result.Hair.PieceIds.RemoveAll(id => catalog.Part(id).Slot == Slots.HairFront && !CharacterGenerator.Compatible(catalog.Part(id), backs)) > 0)
                fixes.Add("bangs the cut can't take");
            if (result.Hair.PieceIds.Count(id => catalog.Part(id).Slot == Slots.HairBack) != 1)
            {
                result.Hair.PieceIds = fresh.Hair.PieceIds.ToList();
                fixes.Add("hairstyle");
            }

            // Outfits: known contexts only, each one completed from the fresh outfit for that context.
            List<PartEntry> hair = result.Hair.PieceIds.Select(catalog.Part).ToList();
            var outfits = new List<OutfitDna>();
            foreach (OutfitContext context in catalog.Contexts)
            {
                OutfitDna fresher = fresh.Outfits.FirstOrDefault(o => o.Context == context.Id);
                OutfitDna mine = result.Outfits.FirstOrDefault(o => o.Context == context.Id);
                if (mine == null)
                {
                    if (fresher != null) outfits.Add(fresher);
                    fixes.Add($"new outfit for '{context.Id}'");
                    continue;
                }
                int count = mine.Items.Count, added = 0;
                mine.Items = mine.Items.Where(i => catalog.Part(i.ItemId) is PartEntry p && (p.Contexts.Count == 0 || p.Contexts.Contains(context.Id))
                    && (context.OnlyTags.Count == 0 || p.Tags.Any(context.OnlyTags.Contains) || p.Slot == Slots.Glasses || p.Slot == Slots.Earrings)).ToList();
                foreach (ItemDna item in mine.Items) FixColours(catalog, item);
                bool Has(string slot) => mine.Items.Any(i => catalog.Part(i.ItemId).Slot == slot);
                bool dressed = Has(Slots.OnePiece) || (Has(Slots.Top) && Has(Slots.Bottom));
                if (!dressed || (context.Shoes && !Has(Slots.Shoes)) || (context.Outer && !Has(Slots.Outer)))
                {
                    // Something essential went missing: take the fresh outfit's pieces for the empty slots.
                    foreach (ItemDna item in fresher?.Items ?? new List<ItemDna>())
                    {
                        PartEntry part = catalog.Part(item.ItemId);
                        bool empty = !Has(part.Slot) && !(part.Slot == Slots.OnePiece && (Has(Slots.Top) || Has(Slots.Bottom)))
                            && !((part.Slot == Slots.Top || part.Slot == Slots.Bottom) && Has(Slots.OnePiece));
                        if (!empty) continue;
                        mine.Items.Add(item);
                        added++;
                    }
                }
                if (mine.Items.Count - added != count || added > 0) fixes.Add($"pieces in the '{context.Id}' outfit");
                outfits.Add(mine);
            }
            if (result.Outfits.Any(o => catalog.Context(o.Context) == null)) fixes.Add("outfits for contexts this build doesn't have");
            result.Outfits = outfits;
            generator.DropIncompatibleParts(result);

            if (DnaValidator.Validate(catalog, result).Count == 0) return result;
            fixes.Add("everything else: a fresh character from the same seed");
            return fresh;
        }

        static void FixColours(ContentCatalog catalog, ItemDna item)
        {
            PartEntry part = catalog.Part(item.ItemId);
            while (item.RegionColors.Count < part.Regions) item.RegionColors.Add(item.RegionColors.Count > 0 ? item.RegionColors[0] : new HsvColor(0f, 0f, 0.5f));
            if (item.RegionColors.Count > part.Regions) item.RegionColors.RemoveRange(part.Regions, item.RegionColors.Count - part.Regions);
            for (int i = 0; i < item.RegionColors.Count; i++) item.RegionColors[i] = Clamp(item.RegionColors[i]);
            if (item.PatternId != null && (!part.Patternable || catalog.Patterns.All(p => p.Id != item.PatternId)))
            {
                item.PatternId = null;
                item.PatternColor = null;
            }
        }

        static string Known(List<StyleEntry> styles, string id, string fallback, string kind, List<string> fixes)
        {
            if (styles.Count == 0 || styles.Any(s => s.Id == id)) return id;
            fixes.Add($"{kind} '{id}'");
            return fallback;
        }

        static HsvColor Clamp(HsvColor c) => new HsvColor(float.IsNaN(c.H) ? 0f : c.H - (float)Math.Floor(c.H), Clamp01(c.S), Clamp01(c.V));
        static float Clamp(float value, float min, float max) => float.IsNaN(value) ? (min + max) / 2f : value < min ? min : value > max ? max : value;
        static float Clamp01(float value) => Clamp(value, 0f, 1f);
    }
}
