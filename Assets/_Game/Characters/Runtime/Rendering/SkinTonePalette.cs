using System;
using UnityEngine;

namespace Game.Characters.Rendering
{
    /// <summary>
    /// The curated skin-tone ramp (PLAN.md §3.4). DNA stores a position on it (Tone 0..1, light to dark) plus an
    /// undertone (0 cool .. 1 warm). Every stop's shade and rim colours are picked by hand under toon lighting:
    /// computing shade as a darker base is what makes dark skin look muddy in toon shaders.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Characters/Skin Tone Palette")]
    public sealed class SkinTonePalette : ScriptableObject
    {
        [Serializable]
        public struct Stop
        {
            public Color Base;
            public Color Shade;
            public Color Rim;
        }

        [Tooltip("Light to dark, evenly spaced along Tone 0..1.")]
        public Stop[] Stops = Array.Empty<Stop>();

        [Tooltip("Hue shift at the cool (0) and warm (1) undertone extremes.")]
        [Range(0f, 0.05f)] public float UndertoneHueShift = 0.018f;

        public Stop Evaluate(float tone, float undertone)
        {
            if (Stops.Length == 0) throw new InvalidOperationException($"Skin tone palette '{name}' has no stops.");

            Stop a = Stops[0], b = Stops[0];
            float blend = 0f;
            if (Stops.Length > 1)
            {
                float position = Mathf.Clamp01(tone) * (Stops.Length - 1);
                int index = Mathf.Min((int)position, Stops.Length - 2);
                a = Stops[index];
                b = Stops[index + 1];
                blend = position - index;
            }

            // Blend in gamma space, where the hand-picked steps are perceptually even.
            float hueShift = (Mathf.Clamp01(undertone) - 0.5f) * 2f * UndertoneHueShift;
            return new Stop
            {
                Base = ShiftHue(Color.Lerp(a.Base, b.Base, blend), hueShift),
                Shade = ShiftHue(Color.Lerp(a.Shade, b.Shade, blend), hueShift),
                Rim = ShiftHue(Color.Lerp(a.Rim, b.Rim, blend), hueShift),
            };
        }

        // Warm moves towards yellow (hue up from red), cool towards pink (hue down).
        static Color ShiftHue(Color colour, float shift)
        {
            Color.RGBToHSV(colour, out float h, out float s, out float v);
            return Color.HSVToRGB(Mathf.Repeat(h + shift, 1f), s, v);
        }
    }
}
