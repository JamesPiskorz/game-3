using System.Collections.Generic;
using Game.Characters.Rng;
using NUnit.Framework;

namespace Game.Characters.Tests.Rng
{
    public class SeedStreamsTests
    {
        [Test]
        public void SubSeeds_NeverChange()
        {
            // If this fails, every existing seed now generates a different character.
            // Only update these values deliberately.
            Assert.That(SeedStreams.SubSeed(42, "hair"), Is.EqualTo(GoldenHair42));
            Assert.That(SeedStreams.SubSeed(42, "skin"), Is.EqualTo(GoldenSkin42));
        }

        [Test]
        public void Categories_DrawFromIndependentStreams()
        {
            uint[] skinFirst = Draw(SeedStreams.For(7, "skin"), 16);

            Draw(SeedStreams.For(7, "hair"), 1000); // e.g. a new hairstyle adds draws to the hair category
            uint[] skinAgain = Draw(SeedStreams.For(7, "skin"), 16);

            Assert.That(skinAgain, Is.EqualTo(skinFirst));
            Assert.That(Draw(SeedStreams.For(7, "hair"), 16), Is.Not.EqualTo(skinFirst));
        }

        [Test]
        public void NeighbouringSeeds_GiveDistinctSubSeeds()
        {
            var seen = new HashSet<ulong>();
            for (ulong seed = 0; seed < 10000; seed++)
                Assert.That(seen.Add(SeedStreams.SubSeed(seed, "face")), $"collision at seed {seed}");
        }

        static uint[] Draw(Pcg32 rng, int count)
        {
            var values = new uint[count];
            for (int i = 0; i < count; i++) values[i] = rng.NextUInt();
            return values;
        }

        const ulong GoldenHair42 = 0x76eca7eecbe53d14UL;
        const ulong GoldenSkin42 = 0x1fc95ef3f0997864UL;
    }
}
