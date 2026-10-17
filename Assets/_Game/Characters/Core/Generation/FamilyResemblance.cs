using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Rng;

namespace Game.Characters.Generation
{
    /// <summary>
    /// Siblings: their own people (style, presentation, hairstyle, clothes) who clearly share a family look.
    /// - Skin tone stays close, so the outfit's skin-contrast rule still sees the real tone.
    /// - Natural hair colour and eye colour carry over. Someone with dyed hair breaks the hair link, as in life.
    /// - The face usually starts from the same preset and leans towards the relative's features.
    /// - The build follows theirs: each body slider keeps its own presentation's average and borrows half the
    ///   relative's difference from theirs, so a tall family has tall daughters and sons.
    /// Everything family-related draws from its own "family" stream, so the rest of the seed is the same character
    /// it would have been.
    /// </summary>
    public static class FamilyResemblance
    {
        /// <summary>How far skin tone may drift between siblings, either way.</summary>
        public const float ToneSpread = 0.035f;

        public const float SamePresetChance = 0.75f;
        public const float FaceLean = 0.55f;
        public const float BuildLean = 0.5f;

        public static CharacterDna GenerateRelative(this CharacterGenerator generator, CharacterDna relative, ulong seed, GenerationRequest request = null)
        {
            ContentCatalog catalog = generator.Catalog;
            Pcg32 rng = SeedStreams.For(seed, "family");

            // Skin goes in through the request, so the outfit is coloured against the tone they'll really have.
            GenerationRequest family = (request ?? GenerationRequest.Any).Clone();
            float min = Math.Max(family.ToneMin, relative.Skin.Tone - ToneSpread), max = Math.Min(family.ToneMax, relative.Skin.Tone + ToneSpread);
            if (min <= max)
            {
                family.ToneMin = min;
                family.ToneMax = max;
            }
            CharacterDna dna = generator.Generate(seed, family);
            dna.Skin.Undertone = Clamp01(relative.Skin.Undertone + rng.NextNormal() * 0.06f);

            Build(catalog, dna, relative);
            Face(catalog, rng, dna, relative);
            Hair(catalog, rng, dna, relative);
            return dna;
        }

        static void Build(ContentCatalog catalog, CharacterDna dna, CharacterDna relative)
        {
            foreach (SliderEntry slider in catalog.BodySliders)
            {
                if (!relative.Body.Sliders.TryGetValue(slider.Id, out float theirs) || !dna.Body.Sliders.TryGetValue(slider.Id, out float mine)) continue;
                float theirMean = Lerp(slider.MeanMasculine, slider.MeanFeminine, relative.Body.Femininity);
                float myMean = Lerp(slider.MeanMasculine, slider.MeanFeminine, dna.Body.Femininity);
                float deviation = Lerp(mine - myMean, theirs - theirMean, BuildLean);
                dna.Body.Sliders[slider.Id] = Clamp(myMean + deviation, slider.Min, slider.Max);
            }
        }

        static void Face(ContentCatalog catalog, Pcg32 rng, CharacterDna dna, CharacterDna relative)
        {
            FacePreset mine = catalog.FacePresets.FirstOrDefault(p => p.Id == dna.Face.PresetId);
            FacePreset theirs = catalog.FacePresets.FirstOrDefault(p => p.Id == relative.Face.PresetId);
            if (theirs != null && rng.Chance(SamePresetChance)) dna.Face.PresetId = theirs.Id;
            FacePreset chosen = catalog.FacePresets.FirstOrDefault(p => p.Id == dna.Face.PresetId) ?? mine;

            // Blend the absolute feature values, then store them as offsets from whichever preset they ended up with.
            float lean = theirs != null && chosen == theirs ? FaceLean : FaceLean * 0.6f;
            foreach (SliderEntry slider in catalog.FaceSliders)
            {
                float Absolute(FacePreset preset, CharacterDna who) =>
                    (preset != null && preset.Values.TryGetValue(slider.Id, out float v) ? v : 0f)
                    + (who.Face.Sliders.TryGetValue(slider.Id, out float offset) ? offset : 0f);
                float target = Lerp(Absolute(mine, dna), Absolute(theirs, relative), lean);
                float basis = chosen != null && chosen.Values.TryGetValue(slider.Id, out float b) ? b : 0f;
                dna.Face.Sliders[slider.Id] = Clamp(target, slider.Min, slider.Max) - basis;
            }

            if (rng.Chance(0.6f) && relative.Face.EyeStyleId != null) dna.Face.EyeStyleId = relative.Face.EyeStyleId;
            if (rng.Chance(0.5f) && relative.Face.BrowStyleId != null) dna.Face.BrowStyleId = relative.Face.BrowStyleId;

            // Natural eye colour runs in the family; fantasy eyes are their own.
            List<PaletteColour> natural = catalog.Palette(catalog.Eyes.NaturalPalette)?.Colours;
            if (IsFrom(natural, dna.Face.IrisLeft) && IsFrom(natural, relative.Face.IrisLeft))
            {
                bool odd = HsvColor.Distance(dna.Face.IrisLeft, dna.Face.IrisRight) > 1e-4f;
                dna.Face.IrisLeft = Colours.Jitter(rng, relative.Face.IrisLeft, 0.005f, 0.03f, 0.03f);
                if (!odd) dna.Face.IrisRight = dna.Face.IrisLeft;
            }
        }

        static void Hair(ContentCatalog catalog, Pcg32 rng, CharacterDna dna, CharacterDna relative)
        {
            List<PaletteColour> natural = catalog.Palette(catalog.Hair.NaturalPalette)?.Colours;
            if (!IsFrom(natural, dna.Hair.Base) || !IsFrom(natural, relative.Hair.Base)) return;

            HsvColor old = dna.Hair.Base;
            bool fadedTips = dna.Hair.Gradient > 0f && HsvColor.Distance(dna.Hair.Tip, Colours.Shift(old, 0.02f, -0.15f, 0.3f)) < 1e-4f;
            HsvColor colour = Colours.Jitter(rng, relative.Hair.Base, 0.006f, 0.04f, 0.05f);
            dna.Hair.Base = colour;
            dna.Hair.Highlight = Colours.Shift(colour, 0f, -0.1f, 0.18f);
            if (dna.Hair.Gradient <= 0f) dna.Hair.Tip = colour;
            else if (fadedTips) dna.Hair.Tip = Colours.Shift(colour, 0.02f, -0.15f, 0.3f);
        }

        /// <summary>Whether a generated colour came from this palette (it was picked from it, then jittered a little).</summary>
        static bool IsFrom(List<PaletteColour> palette, HsvColor colour) =>
            palette != null && palette.Any(c => HsvColor.Distance(HsvColor.FromHex(c.Hex), colour) < 0.12f);

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        static float Clamp01(float value) => Clamp(value, 0f, 1f);
    }
}
