using System;
using System.Collections.Generic;

namespace Game.Characters.Rng
{
    /// <summary>
    /// PCG32 (XSH-RR, matching the reference pcg_basic.c). Integer-only, so a seed produces the same
    /// sequence on every platform and runtime. Character generation must never use UnityEngine.Random
    /// (global state) or System.Random (implementation may change between runtimes).
    /// </summary>
    public sealed class Pcg32
    {
        const ulong Multiplier = 6364136223846793005UL;

        ulong _state;
        readonly ulong _increment;

        /// <param name="seed">Starting state.</param>
        /// <param name="stream">Selects one of 2^63 independent sequences.</param>
        public Pcg32(ulong seed, ulong stream)
        {
            _increment = (stream << 1) | 1UL;
            _state = 0UL;
            NextUInt();
            unchecked { _state += seed; }
            NextUInt();
        }

        public uint NextUInt()
        {
            unchecked
            {
                ulong old = _state;
                _state = old * Multiplier + _increment;
                uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
                int rotation = (int)(old >> 59);
                return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
            }
        }

        /// <summary>Uniform integer in [0, <paramref name="bound"/>), without modulo bias.</summary>
        public int NextInt(int bound)
        {
            if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound), bound, "Must be positive.");

            uint b = (uint)bound;
            uint threshold = unchecked(0u - b) % b; // (2^32 - b) % b: values below this would bias the result
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return (int)(r % b);
            }
        }

        /// <summary>Uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
        public int Range(int minInclusive, int maxExclusive) => minInclusive + NextInt(maxExclusive - minInclusive);

        /// <summary>Uniform float in [0, 1). Built from the top 24 bits, so every result is exactly representable.</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform float between <paramref name="min"/> and <paramref name="max"/>.</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>True with probability <paramref name="probability"/> (0..1).</summary>
        public bool Chance(float probability) => NextFloat() < probability;

        public T Pick<T>(IReadOnlyList<T> items) => items[NextInt(items.Count)];

        /// <summary>
        /// Approximately standard-normal, bounded to ±2√3 (Irwin–Hall, sum of four uniforms). Uses only addition and
        /// multiplication, so it stays bit-identical across runtimes, which Box–Muller's log/cos would not.
        /// </summary>
        public float NextNormal() => (NextFloat() + NextFloat() + NextFloat() + NextFloat() - 2f) * 1.7320508f;

        /// <summary>Picks an item with probability proportional to <paramref name="weight"/>; default when every weight is zero.</summary>
        public T PickWeighted<T>(IReadOnlyList<T> items, Func<T, float> weight)
        {
            float total = 0f;
            for (int i = 0; i < items.Count; i++) total += Math.Max(0f, weight(items[i]));
            if (total <= 0f) return default;

            float target = NextFloat() * total;
            for (int i = 0; i < items.Count; i++)
            {
                float w = Math.Max(0f, weight(items[i]));
                if (w <= 0f) continue;
                if (target < w) return items[i];
                target -= w;
            }
            for (int i = items.Count - 1; i >= 0; i--) // rounding left a sliver: take the last positive one
                if (weight(items[i]) > 0f) return items[i];
            return default;
        }
    }
}
