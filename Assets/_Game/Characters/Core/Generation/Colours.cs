using System.Collections.Generic;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Rng;

namespace Game.Characters.Generation
{
    public static class Colours
    {
        public static HsvColor Pick(Pcg32 rng, IReadOnlyList<PaletteColour> colours) =>
            HsvColor.FromHex(rng.PickWeighted(colours, c => c.Weight).Hex);

        /// <summary>Small random variation so two characters picking the same swatch still differ.</summary>
        public static HsvColor Jitter(Pcg32 rng, HsvColor colour, float hue, float saturation, float value) =>
            new HsvColor(colour.H + rng.Range(-hue, hue), colour.S + rng.Range(-saturation, saturation), colour.V + rng.Range(-value, value)).Normalized();

        public static HsvColor Shift(HsvColor colour, float hue, float saturation, float value) =>
            new HsvColor(colour.H + hue, colour.S + saturation, colour.V + value).Normalized();
    }
}
