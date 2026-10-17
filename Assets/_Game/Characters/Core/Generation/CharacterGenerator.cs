using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Rng;

namespace Game.Characters.Generation
{
    /// <summary>
    /// seed + request → DNA (PLAN.md §3.8). Deterministic and pure C#. Taste comes from the catalog: a style
    /// archetype weights every tagged pick, faces are designed presets plus bounded noise, colours come from curated
    /// palettes through <see cref="OutfitColourer"/>, and slot/tag rules keep outfits wearable. Each slider and each
    /// category draws from its own seed stream, so adding content doesn't reshuffle the rest of a seed.
    /// </summary>
    public sealed class CharacterGenerator
    {
        readonly ContentCatalog _catalog;

        public CharacterGenerator(ContentCatalog catalog)
        {
            List<string> errors = catalog.Validate();
            if (errors.Count > 0)
                throw new ArgumentException("Content catalog has errors:\n" + string.Join("\n", errors), nameof(catalog));
            _catalog = catalog;
        }

        public ContentCatalog Catalog => _catalog;

        public CharacterDna Generate(ulong seed, GenerationRequest request = null)
        {
            request = request ?? GenerationRequest.Any;
            var dna = new CharacterDna { Seed = seed };

            Pcg32 identity = SeedStreams.For(seed, "identity");
            StyleArchetype archetype = PickArchetype(identity, request);
            dna.Style = archetype.Id;
            dna.Body.Femininity = PickFemininity(identity, request);

            GenerateBody(dna, seed);
            GenerateFace(dna, archetype, seed);
            GenerateSkin(dna, seed, request);
            GenerateHair(dna, archetype, seed, request);
            GenerateOutfits(dna, archetype, seed, request);
            return dna;
        }

        /// <summary>
        /// Rerolls the <paramref name="reroll"/> categories of <paramref name="current"/> and keeps the rest, keeping
        /// the character's style and presentation so the result still looks like one person. Parts that the kept and
        /// new pieces no longer agree on (a hat over hair that can't take one) are dropped.
        /// </summary>
        public CharacterDna Reroll(CharacterDna current, DnaCategories reroll, ulong seed, GenerationRequest request = null)
        {
            GenerationRequest keepIdentity = (request ?? GenerationRequest.Any).Clone();
            keepIdentity.Archetype = current.Style != null && _catalog.Archetype(current.Style) != null ? current.Style : keepIdentity.Archetype;
            keepIdentity.FemininityMin = keepIdentity.FemininityMax = current.Body.Femininity;
            CharacterDna fresh = Generate(seed, keepIdentity);

            CharacterDna result = DnaSerializer.FromJson(DnaSerializer.ToJson(current, indented: false));
            if ((reroll & DnaCategories.Body) != 0) result.Body = fresh.Body;
            if ((reroll & DnaCategories.Face) != 0) result.Face = fresh.Face;
            if ((reroll & DnaCategories.Skin) != 0) result.Skin = fresh.Skin;
            if ((reroll & DnaCategories.Hair) != 0) result.Hair = fresh.Hair;
            if ((reroll & DnaCategories.Outfits) != 0) result.Outfits = fresh.Outfits;
            if (reroll == DnaCategories.All) result.Seed = seed;

            DropIncompatibleParts(result);
            return result;
        }

        // ---------------------------------------------------------------- identity

        StyleArchetype PickArchetype(Pcg32 rng, GenerationRequest request)
        {
            if (request.Archetype != null)
                return _catalog.Archetype(request.Archetype) ?? throw new ArgumentException($"Unknown archetype '{request.Archetype}'.");
            return rng.PickWeighted(_catalog.Archetypes, a => a.Weight);
        }

        float PickFemininity(Pcg32 rng, GenerationRequest request)
        {
            float min = Clamp01(request.FemininityMin), max = Clamp01(Math.Max(request.FemininityMin, request.FemininityMax));
            if (min > 0f || max < 1f) return rng.Range(min, max);

            PresentationEntry presentation = _catalog.Presentation;
            float roll = rng.NextFloat();
            if (roll < presentation.FeminineShare)
                return Clamp01(presentation.FeminineCentre + rng.NextNormal() * presentation.Spread);
            if (roll < presentation.FeminineShare + presentation.MasculineShare)
                return Clamp01(presentation.MasculineCentre + rng.NextNormal() * presentation.Spread);
            return rng.Range(0.3f, 0.7f); // androgynous
        }

        // ---------------------------------------------------------------- body, face, skin

        void GenerateBody(CharacterDna dna, ulong seed)
        {
            float femininity = dna.Body.Femininity;
            foreach (SliderEntry slider in _catalog.BodySliders)
            {
                Pcg32 rng = SeedStreams.For(seed, "body." + slider.Id);
                float mean = Lerp(slider.MeanMasculine, slider.MeanFeminine, femininity);
                dna.Body.Sliders[slider.Id] = Clamp(mean + rng.NextNormal() * slider.Spread, slider.Min, slider.Max);
            }
        }

        void GenerateFace(CharacterDna dna, StyleArchetype archetype, ulong seed)
        {
            Pcg32 rng = SeedStreams.For(seed, "face");
            FacePreset preset = rng.PickWeighted(_catalog.FacePresets, p => p.Weight * Affinity.Of(archetype, p.Tags));
            dna.Face.PresetId = preset.Id;

            // Stored as offsets from the preset, clamped so preset + offset stays in the safe range.
            foreach (SliderEntry slider in _catalog.FaceSliders)
            {
                Pcg32 sliderRng = SeedStreams.For(seed, "face." + slider.Id);
                preset.Values.TryGetValue(slider.Id, out float presetValue);
                float value = Clamp(presetValue + sliderRng.NextNormal() * slider.Spread, slider.Min, slider.Max);
                dna.Face.Sliders[slider.Id] = value - presetValue;
            }

            dna.Face.EyeStyleId = rng.PickWeighted(_catalog.EyeStyles, s => s.Weight * Affinity.Of(archetype, s.Tags))?.Id;
            dna.Face.BrowStyleId = rng.PickWeighted(_catalog.BrowStyles, s => s.Weight * Affinity.Of(archetype, s.Tags))?.Id;
            dna.Face.LashStyleId = rng.PickWeighted(_catalog.LashStyles, s => s.Weight * Affinity.Of(archetype, s.Tags))?.Id;

            ColourPalette eyes = _catalog.Palette(rng.Chance(archetype.FantasyEyeChance) ? _catalog.Eyes.FantasyPalette : _catalog.Eyes.NaturalPalette);
            dna.Face.IrisLeft = Colours.Jitter(rng, Colours.Pick(rng, eyes.Colours), 0.01f, 0.05f, 0.05f);
            dna.Face.IrisRight = rng.Chance(_catalog.Eyes.HeterochromiaChance)
                ? Colours.Jitter(rng, Colours.Pick(rng, eyes.Colours), 0.01f, 0.05f, 0.05f)
                : dna.Face.IrisLeft;
        }

        void GenerateSkin(CharacterDna dna, ulong seed, GenerationRequest request)
        {
            Pcg32 rng = SeedStreams.For(seed, "skin");
            float min = Clamp01(request.ToneMin), max = Clamp01(Math.Max(request.ToneMin, request.ToneMax));
            dna.Skin.Tone = rng.Range(min, max); // uniform over the whole ramp: every tone is equally common
            dna.Skin.Undertone = rng.NextFloat();
        }

        // ---------------------------------------------------------------- hair

        void GenerateHair(CharacterDna dna, StyleArchetype archetype, ulong seed, GenerationRequest request)
        {
            Pcg32 rng = SeedStreams.For(seed, "hair");
            var worn = new List<PartEntry>();
            PartEntry back = PickPart(rng, Slots.HairBack, archetype, null, dna.Body.Femininity, worn)
                ?? throw new InvalidOperationException("No hair.back part can be picked.");
            worn.Add(back);
            dna.Hair.PieceIds.Add(back.Id);
            if (!rng.Chance(archetype.NoBangsChance))
            {
                PartEntry front = PickPart(rng, Slots.HairFront, archetype, null, dna.Body.Femininity, worn);
                if (front != null) dna.Hair.PieceIds.Add(front.Id);
            }

            bool fantasy = request.FantasyHair ?? rng.Chance(archetype.FantasyHairChance);
            ColourPalette palette = _catalog.Palette(fantasy ? _catalog.Hair.FantasyPalette : _catalog.Hair.NaturalPalette);
            HsvColor colour = Colours.Jitter(rng, Colours.Pick(rng, palette.Colours), 0.008f, 0.05f, 0.05f);
            dna.Hair.Base = colour;
            dna.Hair.Highlight = Colours.Shift(colour, 0f, -0.1f, 0.18f);

            if (rng.Chance(archetype.GradientChance))
            {
                // Tips either lighten (sun-faded) or take a fantasy colour (dip-dye).
                dna.Hair.Tip = rng.Chance(0.5f)
                    ? Colours.Shift(colour, 0.02f, -0.15f, 0.3f)
                    : Colours.Jitter(rng, Colours.Pick(rng, _catalog.Palette(_catalog.Hair.FantasyPalette).Colours), 0.008f, 0.05f, 0.05f);
                dna.Hair.Gradient = rng.Range(0.6f, 1f);
            }
            else
            {
                dna.Hair.Tip = colour;
                dna.Hair.Gradient = 0f;
            }
        }

        // ---------------------------------------------------------------- outfits

        void GenerateOutfits(CharacterDna dna, StyleArchetype archetype, ulong seed, GenerationRequest request)
        {
            float femininity = dna.Body.Femininity;
            List<PartEntry> hair = dna.Hair.PieceIds.Select(_catalog.Part).ToList();
            ColourPalette palette = _catalog.Palette(archetype.OutfitPalette);

            // Glasses belong to the person, not the outfit: the same pair in every context.
            Pcg32 accessories = SeedStreams.For(seed, "accessories");
            PartEntry glasses = accessories.Chance(archetype.GlassesChance)
                ? PickPart(accessories, Slots.Glasses, archetype, null, femininity, hair)
                : null;
            HsvColor glassesColour = Colours.Jitter(accessories, Colours.Pick(accessories, palette.Colours.Where(c => c.Neutral).DefaultIfEmpty(palette.Colours[0]).ToList()), 0.01f, 0.05f, 0.05f);

            IEnumerable<string> contexts = request.Contexts ?? _catalog.Contexts.Select(c => c.Id);
            foreach (string contextId in contexts)
            {
                OutfitContext context = _catalog.Context(contextId) ?? throw new ArgumentException($"Unknown outfit context '{contextId}'.");
                Pcg32 rng = SeedStreams.For(seed, "outfit." + contextId);
                var worn = new List<PartEntry>(hair);
                var items = new List<PartEntry>();

                void Wear(PartEntry part)
                {
                    if (part == null) return;
                    worn.Add(part);
                    items.Add(part);
                }

                // The outer layer draws from its own stream, so adding it changed no one's existing outfit. Where the
                // context calls for one (a rainy day) it's chosen first, and the rest of the outfit is what goes under it.
                Pcg32 outer = SeedStreams.For(seed, "outfit.outer." + contextId);
                if (context.Outer) Wear(PickPart(outer, Slots.Outer, archetype, context, femininity, worn));

                PartEntry onePiece = ChooseOnePiece(rng, archetype, context, femininity, worn);
                if (onePiece != null) Wear(onePiece);
                else
                {
                    Wear(PickPart(rng, Slots.Top, archetype, context, femininity, worn) ?? throw new InvalidOperationException($"No top fits context '{contextId}'."));
                    Wear(PickPart(rng, Slots.Bottom, archetype, context, femininity, worn) ?? throw new InvalidOperationException($"No bottom fits context '{contextId}'."));
                }
                if (context.Shoes)
                    Wear(PickPart(rng, Slots.Shoes, archetype, context, femininity, worn) ?? throw new InvalidOperationException($"No shoes fit context '{contextId}'."));
                if (rng.Chance(archetype.HatChance))
                    Wear(PickPart(rng, Slots.Hat, archetype, context, femininity, worn));
                if (glasses != null && Compatible(glasses, worn))
                    Wear(glasses);
                // Neckwear draws from its own stream, so adding it changed no one's existing outfit.
                Pcg32 neck = SeedStreams.For(seed, "outfit.neck." + contextId);
                if (neck.Chance(archetype.NeckChance))
                    Wear(PickPart(neck, Slots.Neck, archetype, context, femininity, worn));
                if (!context.Outer && outer.Chance(archetype.OuterChance))
                    Wear(PickPart(outer, Slots.Outer, archetype, context, femininity, worn));

                var outfit = new OutfitDna { Context = contextId };
                OutfitColourer.Colour(rng, _catalog, archetype, palette, items, outfit, SkinColour(dna.Skin.Tone));
                foreach (ItemDna item in outfit.Items.Where(i => i.ItemId == glasses?.Id))
                    item.RegionColors[0] = glassesColour;
                dna.Outfits.Add(outfit);
            }
        }

        /// <summary>The skin's base colour at <paramref name="tone"/>, from the catalog's reference ramp.</summary>
        public HsvColor SkinColour(float tone)
        {
            List<PaletteColour> stops = _catalog.Palette(_catalog.Skin.ReferencePalette).Colours;
            if (stops.Count == 1) return HsvColor.FromHex(stops[0].Hex);
            float position = Clamp01(tone) * (stops.Count - 1);
            int index = Math.Min((int)position, stops.Count - 2);
            HsvColor.FromHex(stops[index].Hex).ToRgb(out float r0, out float g0, out float b0);
            HsvColor.FromHex(stops[index + 1].Hex).ToRgb(out float r1, out float g1, out float b1);
            float t = position - index;
            return HsvColor.FromRgb(r0 + (r1 - r0) * t, g0 + (g1 - g0) * t, b0 + (b1 - b0) * t);
        }

        // A one-piece competes as a single option against "separates", which are the more common choice.
        PartEntry ChooseOnePiece(Pcg32 rng, StyleArchetype archetype, OutfitContext context, float femininity, List<PartEntry> worn)
        {
            List<PartEntry> onePieces = Candidates(Slots.OnePiece, context, worn).ToList();
            if (onePieces.Count == 0) return null;
            float onePieceWeight = onePieces.Sum(p => PartWeight(p, archetype, context, femininity));
            float separatesWeight = Candidates(Slots.Top, context, worn).Select(p => PartWeight(p, archetype, context, femininity)).DefaultIfEmpty(0f).Max() * 2.5f;
            if (rng.NextFloat() * (onePieceWeight + separatesWeight) >= onePieceWeight) return null;
            return rng.PickWeighted(onePieces, p => PartWeight(p, archetype, context, femininity));
        }

        PartEntry PickPart(Pcg32 rng, string slot, StyleArchetype archetype, OutfitContext context, float femininity, List<PartEntry> worn)
        {
            List<PartEntry> candidates = Candidates(slot, context, worn).ToList();
            return rng.PickWeighted(candidates, p => PartWeight(p, archetype, context, femininity));
        }

        IEnumerable<PartEntry> Candidates(string slot, OutfitContext context, List<PartEntry> worn) =>
            _catalog.PartsInSlot(slot).Where(p =>
                (context == null || p.Contexts.Count == 0 || p.Contexts.Contains(context.Id)) && Compatible(p, worn));

        static float PartWeight(PartEntry part, StyleArchetype archetype, OutfitContext context, float femininity) =>
            part.Weight * Affinity.Of(archetype, part.Tags) * Affinity.Of(context, part.Tags) * Affinity.Of(part.Lean, femininity);

        /// <summary>Slot and tag rules between a candidate and what is already worn (see <see cref="PartEntry"/>).</summary>
        public static bool Compatible(PartEntry part, IReadOnlyCollection<PartEntry> worn)
        {
            foreach (PartEntry other in worn)
            {
                if (other.Slot == part.Slot || other.BlocksSlots.Contains(part.Slot) || part.BlocksSlots.Contains(other.Slot)) return false;
                if (part.ExcludesTags.Any(other.Tags.Contains) || other.ExcludesTags.Any(part.Tags.Contains)) return false;
            }
            return part.RequiresTags.All(tag => worn.Any(other => other.Tags.Contains(tag)));
        }

        /// <summary>Takes off optional parts the rest of the look no longer agrees with (a hat over hair that can't take one).</summary>
        public void DropIncompatibleParts(CharacterDna dna)
        {
            List<PartEntry> hair = dna.Hair.PieceIds.Select(_catalog.Part).Where(p => p != null).ToList();
            foreach (OutfitDna outfit in dna.Outfits)
            {
                bool changed;
                do
                {
                    changed = false;
                    List<PartEntry> worn = hair.Concat(outfit.Items.Select(i => _catalog.Part(i.ItemId)).Where(p => p != null)).ToList();
                    foreach (ItemDna item in outfit.Items.ToList())
                    {
                        PartEntry part = _catalog.Part(item.ItemId);
                        if (part == null || IsRequiredSlot(part.Slot)) continue;
                        if (!Compatible(part, worn.Where(p => p != part).ToList()))
                        {
                            outfit.Items.Remove(item);
                            changed = true;
                            break;
                        }
                    }
                } while (changed);
            }
        }

        static bool IsRequiredSlot(string slot) => slot == Slots.Top || slot == Slots.Bottom || slot == Slots.OnePiece || slot == Slots.Shoes;

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        static float Clamp01(float value) => Clamp(value, 0f, 1f);
    }
}
