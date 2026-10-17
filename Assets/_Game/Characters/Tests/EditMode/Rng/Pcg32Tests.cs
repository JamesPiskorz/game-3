using System;
using Game.Characters.Rng;
using NUnit.Framework;

namespace Game.Characters.Tests.Rng
{
    public class Pcg32Tests
    {
        [Test]
        public void MatchesReferenceImplementation()
        {
            // First outputs of pcg32_srandom_r(&rng, 42u, 54u) in pcg-c-basic's pcg32-demo.
            var rng = new Pcg32(42, 54);
            uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };

            foreach (uint value in expected)
                Assert.That(rng.NextUInt(), Is.EqualTo(value));
        }

        [Test]
        public void SameSeedAndStream_GiveSameSequence()
        {
            var a = new Pcg32(123456789, 7);
            var b = new Pcg32(123456789, 7);

            for (int i = 0; i < 1000; i++)
                Assert.That(a.NextUInt(), Is.EqualTo(b.NextUInt()), $"draw {i}");
        }

        [Test]
        public void DifferentStreams_GiveDifferentSequences()
        {
            var a = new Pcg32(42, 1);
            var b = new Pcg32(42, 2);

            int same = 0;
            for (int i = 0; i < 1000; i++)
                if (a.NextUInt() == b.NextUInt()) same++;

            Assert.That(same, Is.LessThan(3));
        }

        [Test]
        public void NextFloat_StaysInUnitInterval()
        {
            var rng = new Pcg32(1, 1);
            for (int i = 0; i < 100000; i++)
            {
                float value = rng.NextFloat();
                Assert.That(value >= 0f && value < 1f, $"{value} at draw {i}");
            }
        }

        [Test]
        public void NextInt_CoversWholeRangeEvenly()
        {
            const int bound = 7;
            const int draws = 70000;
            var counts = new int[bound];
            var rng = new Pcg32(99, 3);

            for (int i = 0; i < draws; i++)
                counts[rng.NextInt(bound)]++;

            foreach (int count in counts)
                Assert.That(count, Is.InRange(draws / bound * 9 / 10, draws / bound * 11 / 10));
        }

        [Test]
        public void NextInt_RejectsNonPositiveBound()
        {
            var rng = new Pcg32(1, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-5));
        }
    }
}
