using System;
using System.Collections.Generic;

namespace Game.Characters.Dna
{
    // Slider maps are sorted with the ordinal comparer so the JSON key order is identical on every
    // machine (the default string comparer is culture-sensitive).

    /// <summary>
    /// Complete description of one character's appearance: the source of truth for saves, presets,
    /// share codes and the creator. Pure data, holding stable content IDs ("hair.front.bob_01", never
    /// list indices) and normalized numbers, so the generator can produce and test it without Unity.
    /// </summary>
    public sealed class CharacterDna
    {
        public int Version = DnaMigrations.CurrentVersion;

        /// <summary>The seed this character was generated from. Provenance only: rebuilding never re-runs the generator.</summary>
        public ulong Seed;

        /// <summary>The style archetype the character was generated with (provenance; the sim can read it too). Null if hand-made.</summary>
        public string Style;

        public BodyDna Body = new BodyDna();
        public FaceDna Face = new FaceDna();
        public SkinDna Skin = new SkinDna();
        public HairDna Hair = new HairDna();

        /// <summary>One outfit per context ("casual", "date", "work", "sleep"...).</summary>
        public List<OutfitDna> Outfits = new List<OutfitDna>();
    }

    public sealed class BodyDna
    {
        /// <summary>Position on the shared body's shape axis: 0 = masculine end, 1 = feminine end.</summary>
        public float Femininity;

        /// <summary>Normalized values keyed by slider ID ("height", "shoulders"...); content maps them to bones and blendshapes.</summary>
        public SortedDictionary<string, float> Sliders = new SortedDictionary<string, float>(StringComparer.Ordinal);
    }

    public sealed class FaceDna
    {
        /// <summary>The face archetype the sliders are offsets from ("face.preset.soft_03").</summary>
        public string PresetId;

        /// <summary>Normalized offsets from the preset, keyed by slider ID ("eye.scale", "jaw.width"...).</summary>
        public SortedDictionary<string, float> Sliders = new SortedDictionary<string, float>(StringComparer.Ordinal);

        public string EyeStyleId;
        public string BrowStyleId;
        public string LashStyleId;

        /// <summary>Different left and right values give heterochromia.</summary>
        public HsvColor IrisLeft;
        public HsvColor IrisRight;

        /// <summary>Shader-composited face layers: blush, lipstick, freckles, moles...</summary>
        public List<OverlayDna> Makeup = new List<OverlayDna>();
    }

    public sealed class SkinDna
    {
        /// <summary>Position on the curated skin-tone ramp: 0 = lightest stop, 1 = darkest.</summary>
        public float Tone;

        /// <summary>0 = cool, 1 = warm.</summary>
        public float Undertone;
    }

    public sealed class HairDna
    {
        /// <summary>Modular pieces worn together, e.g. "hair.back.long_02" + "hair.front.blunt_01" + "hair.extra.ahoge_01".</summary>
        public List<string> PieceIds = new List<string>();

        public HsvColor Base;
        public HsvColor Tip;
        public HsvColor Highlight;

        /// <summary>0 = solid <see cref="Base"/>, 1 = full gradient to <see cref="Tip"/>.</summary>
        public float Gradient;

        /// <summary>Brow and lash colour; null derives it from the hair.</summary>
        public HsvColor? BrowOverride;
    }

    public sealed class OverlayDna
    {
        public string OverlayId;
        public HsvColor Tint;
        public float Opacity;
    }

    public sealed class OutfitDna
    {
        public string Context;
        public List<ItemDna> Items = new List<ItemDna>();
    }

    public sealed class ItemDna
    {
        public string ItemId;

        /// <summary>Colours for the item's recolourable mask regions (up to 4).</summary>
        public List<HsvColor> RegionColors = new List<HsvColor>();

        /// <summary>Pattern for the item's pattern regions; null for plain.</summary>
        public string PatternId;
        public float PatternScale = 1f;
        public HsvColor? PatternColor;
    }
}
