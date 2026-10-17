using Game.Characters.Rng;

namespace Game.Sim
{
    public enum Weather
    {
        Sunny,
        Cloudy,
        Rainy,
    }

    /// <summary>
    /// Each day's weather, from the town's seed: the same town has the same weather on the same day, so nothing
    /// needs saving. Rain changes plans: people skip outdoor haunts for somewhere indoors.
    /// </summary>
    public static class Forecast
    {
        public const float RainChance = 0.2f;
        public const float CloudChance = 0.3f;

        public static Weather For(ulong worldSeed, int day)
        {
            float roll = SeedStreams.For(StableHash.SplitMix64(worldSeed ^ (ulong)day), "weather").NextFloat();
            return roll < RainChance ? Weather.Rainy : roll < RainChance + CloudChance ? Weather.Cloudy : Weather.Sunny;
        }

        public static string Describe(Weather weather) => weather switch
        {
            Weather.Rainy => "Rain",
            Weather.Cloudy => "Cloudy",
            _ => "Sunny",
        };
    }
}
