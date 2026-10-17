using Game.Characters.Rng;
using NUnit.Framework;

namespace Game.Characters.Tests.Rng
{
    public class StableHashTests
    {
        // Reference vectors from the FNV test suite.
        [TestCase("", 0xcbf29ce484222325UL)]
        [TestCase("a", 0xaf63dc4c8601ec8cUL)]
        [TestCase("foobar", 0x85944171f73967e8UL)]
        public void Fnv1a64_MatchesReferenceVectors(string text, ulong expected)
        {
            Assert.That(StableHash.Fnv1a64(text), Is.EqualTo(expected));
        }

        [Test]
        public void SplitMix64_MatchesReferenceSequence()
        {
            // First outputs of Vigna's splitmix64.c seeded with 1234567.
            ulong[] expected = { 6457827717110365317UL, 3203168211198807973UL, 9817491932198370423UL };
            ulong state = 1234567;

            foreach (ulong value in expected)
            {
                Assert.That(StableHash.SplitMix64(state), Is.EqualTo(value));
                unchecked { state += 0x9E3779B97F4A7C15UL; }
            }
        }
    }
}
