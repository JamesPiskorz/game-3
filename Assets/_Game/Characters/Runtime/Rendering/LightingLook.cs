using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Characters.Rendering
{
    /// <summary>
    /// Everything a lighting preset sets: sun, ambient, sky, fog, lamps, character rim and screen grading (graphics plan
    /// G0.4). A value type so presets blend (<see cref="Lerp"/>) and weather can adjust them (<see cref="Overcast"/>).
    /// </summary>
    [Serializable]
    public struct LightingLook
    {
        [Header("Sun")]
        public Color SunColour;
        public float SunIntensity;
        public Vector3 SunAngles;
        [Range(0f, 1f)] public float ShadowStrength;

        [Header("Ambient: sky above, horizon, ground below")]
        public Color AmbientSky;
        public Color AmbientEquator;
        public Color AmbientGround;

        [Header("Sky")]
        public Color SkyTop;
        public Color SkyHorizon;
        [Range(0f, 1f)] public float Clouds;

        [Header("Fog (linear, metres; end 0 = none)")]
        public Color FogColour;
        public float FogStart;
        public float FogEnd;

        [Header("Lamps and lit windows")]
        public bool Lamps;
        public Color LampColour;
        public float LampIntensity;
        [Tooltip("Emission on lit windows and lamp bulbs (HDR multiplier; above ~1.3 it blooms).")]
        public float Glow;

        [Header("Characters")]
        public Color RimColour;
        [Range(0f, 1f), Tooltip("0 day, 1 night: how much of a character's night ramp shows (G1.2).")]
        public float Night;

        [Header("Screen grading (main camera only)")]
        [Range(0f, 1f)] public float Vignette;
        [Range(-100f, 100f)] public float Saturation;
        [Range(-100f, 100f)] public float Contrast;
        public Color ColourFilter;
        public Color SplitShadows;
        public Color SplitHighlights;

        public static LightingLook Lerp(in LightingLook a, in LightingLook b, float t)
        {
            t = Mathf.Clamp01(t);
            return new LightingLook
            {
                SunColour = Color.Lerp(a.SunColour, b.SunColour, t),
                SunIntensity = Mathf.Lerp(a.SunIntensity, b.SunIntensity, t),
                SunAngles = new Vector3(Mathf.LerpAngle(a.SunAngles.x, b.SunAngles.x, t), Mathf.LerpAngle(a.SunAngles.y, b.SunAngles.y, t), Mathf.LerpAngle(a.SunAngles.z, b.SunAngles.z, t)),
                ShadowStrength = Mathf.Lerp(a.ShadowStrength, b.ShadowStrength, t),
                AmbientSky = Color.Lerp(a.AmbientSky, b.AmbientSky, t),
                AmbientEquator = Color.Lerp(a.AmbientEquator, b.AmbientEquator, t),
                AmbientGround = Color.Lerp(a.AmbientGround, b.AmbientGround, t),
                SkyTop = Color.Lerp(a.SkyTop, b.SkyTop, t),
                SkyHorizon = Color.Lerp(a.SkyHorizon, b.SkyHorizon, t),
                Clouds = Mathf.Lerp(a.Clouds, b.Clouds, t),
                FogColour = Color.Lerp(a.FogColour, b.FogColour, t),
                FogStart = Mathf.Lerp(a.FogStart, b.FogStart, t),
                FogEnd = Mathf.Lerp(a.FogEnd, b.FogEnd, t),
                // Lamps come on at the start of the blend into a lamp-lit preset and stay on until the blend out ends.
                Lamps = a.Lamps || b.Lamps,
                LampColour = Color.Lerp(a.LampColour, b.LampColour, t),
                LampIntensity = Mathf.Lerp(a.Lamps ? a.LampIntensity : 0f, b.Lamps ? b.LampIntensity : 0f, t),
                Glow = Mathf.Lerp(a.Glow, b.Glow, t),
                RimColour = Color.Lerp(a.RimColour, b.RimColour, t),
                Night = Mathf.Lerp(a.Night, b.Night, t),
                Vignette = Mathf.Lerp(a.Vignette, b.Vignette, t),
                Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t),
                Contrast = Mathf.Lerp(a.Contrast, b.Contrast, t),
                ColourFilter = Color.Lerp(a.ColourFilter, b.ColourFilter, t),
                SplitShadows = Color.Lerp(a.SplitShadows, b.SplitShadows, t),
                SplitHighlights = Color.Lerp(a.SplitHighlights, b.SplitHighlights, t),
            };
        }

        /// <summary>Indoors: the walls bounce their colour into the room, so half the ambient is the wall's.</summary>
        public LightingLook Walls(Color wall)
        {
            LightingLook look = this;
            look.AmbientSky = Color.Lerp(wall * 0.75f, AmbientSky, 0.5f);
            look.AmbientEquator = Color.Lerp(wall * 0.75f, AmbientEquator, 0.5f);
            look.AmbientGround = Color.Lerp(wall * 0.75f, AmbientGround, 0.5f);
            return look;
        }

        /// <summary>
        /// Clouds or rain over this look: <paramref name="grey"/> 0 (clear) to 1 greys the sky, ambient and sun, softens
        /// the sun and its shadows, and pulls the fog in. Rain uses about 0.65, clouds 0.4.
        /// </summary>
        public LightingLook Overcast(float grey, float shadowStrength)
        {
            if (grey <= 0f) return this;
            LightingLook look = this;
            Color sky = Color.Lerp(new Color(0.5f, 0.54f, 0.6f), new Color(0.06f, 0.07f, 0.09f), Night);
            Color ambient = new Color(0.38f, 0.4f, 0.45f) * Mathf.Lerp(1f, 0.35f, Night);
            look.SkyTop = Color.Lerp(SkyTop, sky, grey);
            look.SkyHorizon = Color.Lerp(SkyHorizon, sky, grey);
            look.Clouds = Mathf.Max(Clouds, grey);
            look.AmbientSky = Color.Lerp(AmbientSky, ambient, grey);
            look.AmbientEquator = Color.Lerp(AmbientEquator, ambient, grey);
            look.AmbientGround = Color.Lerp(AmbientGround, ambient, grey);
            look.SunColour = Color.Lerp(SunColour, new Color(0.82f, 0.86f, 0.92f), grey);
            look.SunIntensity = SunIntensity * (1f - 0.55f * grey);
            look.ShadowStrength = shadowStrength;
            look.FogColour = Color.Lerp(FogColour, sky, grey);
            if (FogEnd > 0f)
            {
                look.FogStart = FogStart * (1f - 0.5f * grey);
                look.FogEnd = FogEnd * (1f - 0.45f * grey);
            }
            look.Saturation = Saturation - 12f * grey;
            return look;
        }
    }
}
