using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Dna;

namespace Game.Characters.Content
{
    /// <summary>
    /// Everything the generator knows about content: parts and their slots and tags, sliders and their
    /// distributions, face presets, palettes, style archetypes and outfit contexts. Pure data (IDs, numbers,
    /// tags), loaded from Content/Catalog.json, so generation runs and is tested without Unity. Engine bindings
    /// (prefabs, bones, textures) live separately in the Unity layer, keyed by the same IDs.
    /// </summary>
    public sealed class ContentCatalog
    {
        public PresentationEntry Presentation = new PresentationEntry();
        public List<SliderEntry> BodySliders = new List<SliderEntry>();
        public List<SliderEntry> FaceSliders = new List<SliderEntry>();
        public List<FacePreset> FacePresets = new List<FacePreset>();
        public List<StyleEntry> EyeStyles = new List<StyleEntry>();
        public List<StyleEntry> BrowStyles = new List<StyleEntry>();
        public List<StyleEntry> LashStyles = new List<StyleEntry>();
        public List<PartEntry> Parts = new List<PartEntry>();
        public List<PatternEntry> Patterns = new List<PatternEntry>();
        public List<ColourPalette> Palettes = new List<ColourPalette>();
        public List<StyleArchetype> Archetypes = new List<StyleArchetype>();
        public List<OutfitContext> Contexts = new List<OutfitContext>();
        public HairRules Hair = new HairRules();
        public EyeRules Eyes = new EyeRules();
        public SkinRules Skin = new SkinRules();

        public PartEntry Part(string id) => Parts.FirstOrDefault(p => p.Id == id);
        public ColourPalette Palette(string id) => Palettes.FirstOrDefault(p => p.Id == id);

        /// <summary>"cloth.top.tshirt_01" → "Tshirt": the last ID segment without its variant number, capitalised.</summary>
        public static string Humanize(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string last = id.Substring(id.LastIndexOf('.') + 1);
            int underscore = last.LastIndexOf('_');
            if (underscore > 0 && int.TryParse(last.Substring(underscore + 1), out _)) last = last.Substring(0, underscore);
            last = last.Replace('_', ' ');
            return char.ToUpperInvariant(last[0]) + last.Substring(1);
        }
        public StyleArchetype Archetype(string id) => Archetypes.FirstOrDefault(a => a.Id == id);
        public OutfitContext Context(string id) => Contexts.FirstOrDefault(c => c.Id == id);
        public IEnumerable<PartEntry> PartsInSlot(string slot) => Parts.Where(p => p.Slot == slot);

        /// <summary>Content errors: dangling references, duplicate IDs, bad ranges. Empty when the catalog is usable.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            void Unique<T>(IEnumerable<T> items, Func<T, string> id, string kind)
            {
                foreach (IGrouping<string, T> group in items.GroupBy(id).Where(g => g.Count() > 1))
                    errors.Add($"Duplicate {kind} id '{group.Key}'.");
            }

            Unique(Parts, p => p.Id, "part");
            Unique(Palettes, p => p.Id, "palette");
            Unique(Archetypes, a => a.Id, "archetype");
            Unique(BodySliders.Concat(FaceSliders), s => s.Id, "slider");
            Unique(FacePresets, p => p.Id, "face preset");
            Unique(Patterns, p => p.Id, "pattern");
            Unique(Contexts, c => c.Id, "context");

            foreach (SliderEntry slider in BodySliders.Concat(FaceSliders))
                if (!(slider.Min < slider.Max)) errors.Add($"Slider '{slider.Id}' has Min >= Max.");
            foreach (FacePreset preset in FacePresets)
                foreach (string slider in preset.Values.Keys)
                    if (FaceSliders.All(s => s.Id != slider)) errors.Add($"Face preset '{preset.Id}' sets unknown slider '{slider}'.");
            foreach (StyleArchetype archetype in Archetypes)
                if (Palette(archetype.OutfitPalette) == null) errors.Add($"Archetype '{archetype.Id}' uses unknown palette '{archetype.OutfitPalette}'.");
            foreach (string palette in new[] { Hair.NaturalPalette, Hair.FantasyPalette, Eyes.NaturalPalette, Eyes.FantasyPalette, Skin.ReferencePalette })
                if (Palette(palette) == null) errors.Add($"Unknown palette '{palette}'.");
            foreach (ColourPalette palette in Palettes)
            {
                if (palette.Colours.Count == 0) errors.Add($"Palette '{palette.Id}' is empty.");
                foreach (PaletteColour colour in palette.Colours)
                    if (!HsvColor.TryParseHex(colour.Hex, out _)) errors.Add($"Palette '{palette.Id}' has bad colour '{colour.Hex}'.");
            }
            foreach (PartEntry part in Parts)
            {
                if (string.IsNullOrEmpty(part.Slot)) errors.Add($"Part '{part.Id}' has no slot.");
                foreach (string context in part.Contexts)
                    if (Context(context) == null) errors.Add($"Part '{part.Id}' names unknown context '{context}'.");
            }
            foreach (string slot in new[] { Slots.Top, Slots.Bottom, Slots.Shoes, Slots.HairBack })
                if (!PartsInSlot(slot).Any()) errors.Add($"No parts for required slot '{slot}'.");
            if (Archetypes.Count == 0) errors.Add("No archetypes.");
            if (FacePresets.Count == 0) errors.Add("No face presets.");
            if (Contexts.Count == 0) errors.Add("No outfit contexts.");
            foreach (OutfitContext context in Contexts.Where(c => c.OnlyTags.Count > 0))
                foreach (string slot in new[] { Slots.Top, Slots.Bottom, Slots.Shoes })
                    if (!PartsInSlot(slot).Any(p => (p.Contexts.Count == 0 || p.Contexts.Contains(context.Id)) && p.Tags.Any(context.OnlyTags.Contains)))
                        errors.Add($"Context '{context.Id}' allows only {string.Join("/", context.OnlyTags)}, but no {slot} part has those tags.");
            foreach (OutfitContext context in Contexts.Where(c => c.Outer))
                if (!PartsInSlot(Slots.Outer).Any(p => p.Contexts.Count == 0 || p.Contexts.Contains(context.Id)))
                    errors.Add($"Context '{context.Id}' needs an outer layer, but no outer part fits it.");
            return errors;
        }
    }

    /// <summary>Well-known slot names the generator reasons about.</summary>
    public static class Slots
    {
        public const string Top = "top";
        public const string Bottom = "bottom";
        public const string OnePiece = "onepiece";
        public const string Shoes = "shoes";
        public const string Hat = "hat";
        public const string Glasses = "glasses";
        public const string Neck = "neck";
        public const string Outer = "outer";
        public const string Legwear = "legwear";
        public const string Earrings = "earrings";
        public const string Bag = "bag";
        public const string HairBack = "hair.back";
        public const string HairFront = "hair.front";
    }

    /// <summary>How body presentation (BodyDna.Femininity) is distributed: two clusters plus an androgynous middle.</summary>
    public sealed class PresentationEntry
    {
        public float FeminineShare = 0.46f;
        public float MasculineShare = 0.44f;
        public float FeminineCentre = 0.85f;
        public float MasculineCentre = 0.15f;
        public float Spread = 0.08f;
    }

    /// <summary>
    /// A normalized slider and its distribution. The mean moves with presentation (masculine at 0, feminine at 1);
    /// Min and Max are the safe range: adult proportions, and what clothing has been tested at.
    /// </summary>
    public sealed class SliderEntry
    {
        public string Id;

        /// <summary>Display name for the creator; null falls back to a name made from the ID.</summary>
        public string Name;
        public float Min = -1f;
        public float Max = 1f;
        public float MeanMasculine;
        public float MeanFeminine;
        public float Spread = 0.25f;
    }

    /// <summary>A designed face: slider values that look good together. Generated faces are a preset plus bounded noise.</summary>
    public sealed class FacePreset
    {
        public string Id;

        /// <summary>Display name for the creator; null falls back to a name made from the ID.</summary>
        public string Name;
        public float Weight = 1f;
        public List<string> Tags = new List<string>();
        public Dictionary<string, float> Values = new Dictionary<string, float>();
    }

    /// <summary>A pickable style ID (eye style, brow style...) with a weight and tags.</summary>
    public sealed class StyleEntry
    {
        public string Id;

        /// <summary>Display name for the creator; null falls back to a name made from the ID.</summary>
        public string Name;
        public float Weight = 1f;
        public List<string> Tags = new List<string>();
    }

    public sealed class PartEntry
    {
        public string Id;

        /// <summary>Display name for the creator; null falls back to a name made from the ID.</summary>
        public string Name;
        public string Slot;
        public List<string> BlocksSlots = new List<string>();
        public float Weight = 1f;

        /// <summary>Style tags ("casual", "gothic"...) that archetypes and contexts weight, plus rule tags ("hat_ok").</summary>
        public List<string> Tags = new List<string>();

        /// <summary>Every tag here must appear on some other worn part (a beanie requires "hat_ok" hair).</summary>
        public List<string> RequiresTags = new List<string>();

        /// <summary>No other worn part may carry any of these tags.</summary>
        public List<string> ExcludesTags = new List<string>();

        /// <summary>-1 masculine .. +1 feminine: scales the weight by how well it suits the body's presentation. Never zero.</summary>
        public float Lean;

        /// <summary>Recolour regions the item uses (1-4); the generator fills this many colours.</summary>
        public int Regions = 1;

        /// <summary>Whether region 1 can take a pattern.</summary>
        public bool Patternable;

        /// <summary>Outfit contexts the item can appear in; empty means any.</summary>
        public List<string> Contexts = new List<string>();
    }

    public sealed class PatternEntry
    {
        public string Id;

        /// <summary>Display name for the creator; null falls back to a name made from the ID.</summary>
        public string Name;
        public float Weight = 1f;
        public List<string> Tags = new List<string>();
        public float MinScale = 6f;
        public float MaxScale = 12f;
    }

    public sealed class ColourPalette
    {
        public string Id;
        public List<PaletteColour> Colours = new List<PaletteColour>();
    }

    public sealed class PaletteColour
    {
        public string Hex;
        public bool Neutral;
        public float Weight = 1f;
    }

    /// <summary>
    /// A style a character dresses and presents in. Its tag weights steer every tagged pick (parts, face presets,
    /// patterns), which is what keeps generated outfits coherent rather than random.
    /// </summary>
    public sealed class StyleArchetype
    {
        public string Id;

        /// <summary>Display name for the creator; null falls back to a name made from the ID.</summary>
        public string Name;
        public float Weight = 1f;
        public Dictionary<string, float> TagWeights = new Dictionary<string, float>();
        public string OutfitPalette;
        public float PatternChance = 0.2f;
        public float FantasyHairChance = 0.15f;
        public float FantasyEyeChance = 0.05f;
        public float GradientChance = 0.15f;
        public float GlassesChance = 0.1f;
        public float NeckChance = 0.1f;

        /// <summary>The chance an outfit adds an outer layer (jacket, coat) by choice; contexts can require one.</summary>
        public float OuterChance = 0.1f;

        /// <summary>The chance of tights or socks with legs that show (anything tagged bare_legs).</summary>
        public float LegwearChance = 0.1f;

        /// <summary>Earrings belong to the person, like glasses: the same pair in every outfit.</summary>
        public float EarringChance = 0.15f;
        public float BagChance = 0.1f;
        public float HatChance = 0.1f;
        public float NoBangsChance = 0.25f;
    }

    public sealed class OutfitContext
    {
        public string Id;
        public string Name;
        public Dictionary<string, float> TagWeights = new Dictionary<string, float>();
        public bool Shoes = true;

        /// <summary>Every outfit for this context has an outer layer (a rainy day's coat).</summary>
        public bool Outer;

        /// <summary>If set, only parts with one of these tags are worn in this context (sportswear at the gym). Hair is the person's own.</summary>
        public List<string> OnlyTags = new List<string>();
    }

    public sealed class HairRules
    {
        public string NaturalPalette = "hair.natural";
        public string FantasyPalette = "hair.fantasy";
    }

    public sealed class EyeRules
    {
        public string NaturalPalette = "eyes.natural";
        public string FantasyPalette = "eyes.fantasy";
        public float HeterochromiaChance = 0.02f;
    }

    /// <summary>
    /// What the generator knows about skin colour: the base colours of the skin ramp, light to dark (the renderer's
    /// SkinTonePalette holds the full art-directed stops). Used to keep clothing from matching the skin it covers,
    /// which reads as bare skin.
    /// </summary>
    public sealed class SkinRules
    {
        public string ReferencePalette = "skin.reference";
        public float MinClothingContrast = 0.2f;
    }
}
