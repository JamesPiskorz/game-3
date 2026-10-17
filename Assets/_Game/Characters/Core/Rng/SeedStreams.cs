namespace Game.Characters.Rng
{
    /// <summary>
    /// Derives an independent random stream per category ("body", "hair", "outfit.date"...) from one
    /// character seed. A category only ever draws from its own stream, so adding content or draws to
    /// one category never reshuffles the others: a seed keeps its skin tone when new hairstyles ship.
    /// </summary>
    public static class SeedStreams
    {
        public static ulong SubSeed(ulong seed, string category) =>
            StableHash.SplitMix64(seed ^ StableHash.Fnv1a64(category));

        public static Pcg32 For(ulong seed, string category) =>
            new Pcg32(SubSeed(seed, category), StableHash.Fnv1a64(category));
    }
}
