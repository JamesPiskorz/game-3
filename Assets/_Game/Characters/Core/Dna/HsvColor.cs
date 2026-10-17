using System;
using System.Globalization;

namespace Game.Characters.Dna
{
    /// <summary>
    /// A colour stored literally in the DNA, not as a palette reference, so editing a palette never
    /// recolours existing characters. Components are 0..1 in sRGB space; hue wraps.
    /// </summary>
    public struct HsvColor
    {
        public float H;
        public float S;
        public float V;

        public HsvColor(float h, float s, float v)
        {
            H = h;
            S = s;
            V = v;
        }

        public override string ToString() => $"HSV({H:0.###}, {S:0.###}, {V:0.###})";

        /// <summary>Wraps hue into 0..1 and clamps saturation and value.</summary>
        public HsvColor Normalized() => new HsvColor(Wrap(H), Clamp01(S), Clamp01(V));

        public static bool TryParseHex(string hex, out HsvColor colour)
        {
            colour = default;
            if (string.IsNullOrEmpty(hex)) return false;
            string digits = hex[0] == '#' ? hex.Substring(1) : hex;
            if (digits.Length != 6 || !int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                return false;
            colour = FromRgb(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
            return true;
        }

        public static HsvColor FromHex(string hex) =>
            TryParseHex(hex, out HsvColor colour) ? colour : throw new FormatException($"'{hex}' is not a #RRGGBB colour.");

        public static HsvColor FromRgb(float r, float g, float b)
        {
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;
            float h = 0f;
            if (delta > 0f)
            {
                if (max == r) h = (g - b) / delta / 6f;
                else if (max == g) h = ((b - r) / delta + 2f) / 6f;
                else h = ((r - g) / delta + 4f) / 6f;
            }
            return new HsvColor(Wrap(h), max > 0f ? delta / max : 0f, max);
        }

        public void ToRgb(out float r, out float g, out float b)
        {
            float h = Wrap(H) * 6f, s = Clamp01(S), v = Clamp01(V);
            int sector = (int)h % 6;
            float f = h - (int)h;
            float p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
            switch (sector)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        /// <summary>Perceptual-ish distance 0..~1.7: hue counts less when either colour is grey.</summary>
        public static float Distance(HsvColor a, HsvColor b)
        {
            float hue = Math.Abs(Wrap(a.H) - Wrap(b.H));
            hue = Math.Min(hue, 1f - hue) * 2f * Math.Min(a.S, b.S);
            float s = a.S - b.S, v = a.V - b.V;
            return (float)Math.Sqrt(hue * hue + s * s + v * v);
        }

        static float Wrap(float value) => value - (float)Math.Floor(value);
        static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
