using System;

namespace Game.Sim
{
    /// <summary>Big Five traits, each 0..1. Archetypes give the means; residents vary around them.</summary>
    public sealed class Personality
    {
        public float Openness = 0.5f;
        public float Conscientiousness = 0.5f;
        public float Extraversion = 0.5f;
        public float Agreeableness = 0.5f;
        public float Neuroticism = 0.5f;

        public Personality Clone() => (Personality)MemberwiseClone();

        /// <summary>
        /// The voice their dialogue is written in. Outgoing and sunny reads bubbly, retiring reads shy, prickly reads
        /// dry, and everyone else is warm.
        /// </summary>
        public string Tone
        {
            get
            {
                if (Agreeableness < 0.45f && Extraversion < 0.6f) return Tones.Dry;
                if (Extraversion < 0.4f) return Tones.Shy;
                if (Extraversion > 0.65f && Neuroticism < 0.55f) return Tones.Bubbly;
                return Tones.Warm;
            }
        }

        internal static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }

    public static class Tones
    {
        public const string Warm = "warm";
        public const string Shy = "shy";
        public const string Bubbly = "bubbly";
        public const string Dry = "dry";
        public const string Any = "any";
    }
}
