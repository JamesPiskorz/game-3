using System.Collections.Generic;
using System.Linq;
using Game.Characters.Rng;

namespace Game.Sim
{
    /// <summary>
    /// The town's occasional events (SimCatalog "events": a black-tie gala, a garden party). Some weeks, by its chance,
    /// an event is held on one of its days, seeded from the town like the weather, so nothing needs saving. It needs its
    /// dress code in the character catalog (formal wear comes with the generated content: on the stand-ins there are
    /// no events), and one held outdoors is called off in the rain. While it's on, residents who are out (not at work,
    /// not at home) go to it, and everyone there wears its outfit (<see cref="SimWorld.OutfitAt"/>).
    /// </summary>
    public static class TownEvents
    {
        /// <summary>The events held on <paramref name="day"/>, in catalog order.</summary>
        public static IEnumerable<EventEntry> On(SimWorld world, int day)
        {
            int week = day / 7;
            foreach (EventEntry entry in world.Catalog.Events)
            {
                if (world.Characters?.Context(entry.Outfit) == null) continue;
                Pcg32 rng = SeedStreams.For(StableHash.SplitMix64(world.Seed ^ ((ulong)week << 20) ^ StableHash.Fnv1a64(entry.Id)), "events");
                if (!rng.Chance(entry.Chance) || rng.Pick(entry.Days) != day % 7) continue;
                LocationEntry venue = world.Catalog.Location(entry.Location);
                if (venue != null && venue.Tags.Contains("outdoors") && Forecast.For(world.Seed, day) == Weather.Rainy) continue;
                yield return entry;
            }
        }

        /// <summary>The event under way at <paramref name="time"/>, if any (the first, should two overlap).</summary>
        public static EventEntry Happening(SimWorld world, SimTime time) =>
            On(world, time.Day).FirstOrDefault(e => time.Hour >= e.Start && time.Hour < e.End);

        /// <summary>The event under way at <paramref name="location"/> at <paramref name="time"/>, if any.</summary>
        public static EventEntry At(SimWorld world, string location, SimTime time) =>
            On(world, time.Day).FirstOrDefault(e => e.Location == location && time.Hour >= e.Start && time.Hour < e.End);
    }
}
