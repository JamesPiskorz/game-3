using System.Collections.Generic;

namespace Game.Sim.Residents
{
    public enum Pronouns
    {
        She,
        He,
        They,
    }

    /// <summary>
    /// Who a resident is, beyond how they look: name, personality, tastes, job and haunts. Derived deterministically
    /// from their character seed, so a town only has to save its world seed to get everyone back.
    /// </summary>
    public sealed class ResidentProfile
    {
        public string FirstName;
        public string FamilyName;
        public Pronouns Pronouns;
        public int Age;

        /// <summary>The character archetype they dress in, which also shapes their personality and interests.</summary>
        public string Archetype;
        public Personality Personality = new Personality();

        /// <summary>Topic ID → how much they like it, -1 (can't stand it) .. +1 (favourite). Missing topics are neutral.</summary>
        public SortedDictionary<string, float> Interests = new SortedDictionary<string, float>(System.StringComparer.Ordinal);

        public string JobId;

        /// <summary>Locations they spend free time at, most favoured first.</summary>
        public List<string> Hangouts = new List<string>();

        /// <summary>Clothing style tag → how much it appeals to them on someone else. Drives first impressions.</summary>
        public SortedDictionary<string, float> StyleTaste = new SortedDictionary<string, float>(System.StringComparer.Ordinal);

        public string Name => FirstName + " " + FamilyName;
        public string Tone => Personality.Tone;

        public float Interest(string topic) => topic != null && Interests.TryGetValue(topic, out float value) ? value : 0f;

        public string Subject => Pronouns == Pronouns.She ? "she" : Pronouns == Pronouns.He ? "he" : "they";
        public string Object => Pronouns == Pronouns.She ? "her" : Pronouns == Pronouns.He ? "him" : "them";
        public string Possessive => Pronouns == Pronouns.She ? "her" : Pronouns == Pronouns.He ? "his" : "their";
    }
}
