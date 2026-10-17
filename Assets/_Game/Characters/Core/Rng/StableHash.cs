using System.Text;

namespace Game.Characters.Rng
{
    /// <summary>
    /// Hashes whose output never changes between runs, machines or runtimes (unlike
    /// <see cref="string.GetHashCode"/>). Seeds depend on these values: changing them changes
    /// every generated character.
    /// </summary>
    public static class StableHash
    {
        const ulong FnvOffsetBasis = 0xcbf29ce484222325UL;
        const ulong FnvPrime = 0x100000001b3UL;

        /// <summary>64-bit FNV-1a over the UTF-8 bytes of <paramref name="text"/>.</summary>
        public static ulong Fnv1a64(string text)
        {
            unchecked
            {
                ulong hash = FnvOffsetBasis;
                foreach (byte b in Encoding.UTF8.GetBytes(text))
                {
                    hash ^= b;
                    hash *= FnvPrime;
                }
                return hash;
            }
        }

        /// <summary>
        /// One step of Vigna's SplitMix64 from state <paramref name="x"/>. Turns structured inputs
        /// (small seeds, xor-ed hashes) into well-mixed 64-bit values.
        /// </summary>
        public static ulong SplitMix64(ulong x)
        {
            unchecked
            {
                ulong z = x + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
