using System;
using System.Collections.Generic;

namespace Game.Characters.Generation
{
    /// <summary>DNA sections that can be rerolled independently ("randomize with locks").</summary>
    [Flags]
    public enum DnaCategories
    {
        None = 0,
        Body = 1,
        Face = 2,
        Skin = 4,
        Hair = 8,
        Outfits = 16,
        All = Body | Face | Skin | Hair | Outfits,
    }

    /// <summary>Optional constraints on generation, e.g. "gothic, feminine-presenting, red hair". Unset = anything.</summary>
    public sealed class GenerationRequest
    {
        public static readonly GenerationRequest Any = new GenerationRequest();

        /// <summary>Style archetype ID; null picks one by weight.</summary>
        public string Archetype;

        public float FemininityMin;
        public float FemininityMax = 1f;
        public float ToneMin;
        public float ToneMax = 1f;

        /// <summary>Force fantasy (true) or natural (false) hair colours; null leaves it to the archetype.</summary>
        public bool? FantasyHair;

        /// <summary>Outfit contexts to dress for; null means every context in the catalog.</summary>
        public List<string> Contexts;

        public GenerationRequest Clone() => (GenerationRequest)MemberwiseClone();
    }
}
