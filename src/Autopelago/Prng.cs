using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autopelago;

public static class Prng
{
    // retrieved with the following header from https://prng.di.unimi.it/xoshiro256plus.c
    /*
    Written in 2018 by David Blackman and Sebastiano Vigna (vigna@acm.org)

    To the extent possible under law, the author has dedicated all copyright
    and related and neighboring rights to this software to the public domain
    worldwide. This software is distributed without any warranty.

    See <http://creativecommons.org/publicdomain/zero/1.0/>.

    This is xoshiro256+ 1.0, our best and fastest generator for floating-point
    numbers. We suggest to use its upper bits for floating-point
    generation, as it is slightly faster than xoshiro256++/xoshiro256**. It
    passes all tests we are aware of except for the lowest three bits,
    which might fail linearity tests (and just those), so if low linear
    complexity is not considered an issue (as it is usually the case) it
    can be used to generate 64-bit outputs, too.

    We suggest to use a sign test to extract a random Boolean value, and
    right shifts to extract subsets of bits.

    The state must be seeded so that it is not everywhere zero. If you have
    a 64-bit seed, we suggest to seed a splitmix64 generator and use its
    output to fill s.
    */
    public static ulong Next(ref State s)
    {
        ulong result = unchecked(s[0] + s[3]);

        ulong t = s[1] << 17;

        s[2] ^= s[0];
        s[3] ^= s[1];
        s[1] ^= s[2];
        s[0] ^= s[3];

        s[2] ^= t;

        s[3] = BitOperations.RotateLeft(s[3], 45);

        return result;
    }

    public static int NextD20(ref State s)
    {
        return ((int)Math.Floor(NextDouble(ref s) * 20)) + 1;
    }

    public static double NextDouble(ref State s)
    {
        ulong n;
        do
        {
            n = Next(ref s);
        } while (n == ulong.MaxValue);
        return n / (double)ulong.MaxValue;
    }

    public static State ShortJumped(State s)
    {
        ShortJump(ref s);
        return s;
    }

    // This is the jump function for the generator. It is equivalent
    // to 2^128 calls to next(); it can be used to generate 2^128
    // non-overlapping subsequences for parallel computations.
    public static void ShortJump(ref State s)
    {
        Jump(ref s, [0x180ec6d33cfd0aba, 0xd5a61266f0c9392c, 0xa9582618e03fc9aa, 0x39abdc4529b1661c]);
    }

    public static State LongJumped(State s)
    {
        LongJump(ref s);
        return s;
    }

    // This is the long-jump function for the generator. It is equivalent to
    // 2^192 calls to next(); it can be used to generate 2^64 starting points,
    // from each of which ShortJump() will generate 2^64 non-overlapping
    // subsequences for parallel distributed computations.
    public static void LongJump(ref State s)
    {
        Jump(ref s, [0x76e15d3efefdcbbf, 0xc5004e441c522fb3, 0x77710069854ee241, 0x39109bb02acbe635]);
    }

    private static void Jump(ref State s, ReadOnlySpan<ulong> jump)
    {
        State ss = default;

        // ReSharper disable once ForCanBeConvertedToForeach
        for (int i = 0; i < jump.Length; i++)
        {
            for (int b = 0; b < 64; b++)
            {
                if ((jump[i] & (1ul << b)) != 0)
                {
                    ss[0] ^= s[0];
                    ss[1] ^= s[1];
                    ss[2] ^= s[2];
                    ss[3] ^= s[3];
                }

                _ = Next(ref s);
            }
        }

        s = ss;
    }

    // retrieved with the following header from https://prng.di.unimi.it/splitmix64.c
    /*
    Written in 2015 by Sebastiano Vigna (vigna@acm.org)

    To the extent possible under law, the author has dedicated all copyright
    and related and neighboring rights to this software to the public domain
    worldwide. This software is distributed without any warranty.

    See <http://creativecommons.org/publicdomain/zero/1.0/>.

    This is a fixed-increment version of Java 8's SplittableRandom generator
    See http://dx.doi.org/10.1145/2714064.2660195 and
    http://docs.oracle.com/javase/8/docs/api/java/util/SplittableRandom.html

    It is a very fast generator passing BigCrush, and it can be useful if
    for some reason you absolutely want 64 bits of state.
    */
    private static ulong NextSplitMix64(ref ulong x)
    {
        unchecked
        {
            ulong z = x += 0x9e3779b97f4a7c15;
            z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9;
            z = (z ^ (z >> 27)) * 0x94d049bb133111eb;
            return z ^ (z >> 31);
        }
    }

    [InlineArray(4)]
    [JsonConverter(typeof(Converter))]
    public struct State
    {
        [SuppressMessage("Style", "IDE0044: Add readonly modifier", Justification = "https://github.com/dotnet/roslyn/issues/69143")]
        private ulong _element0;

        public static State Start(Random? random = null)
        {
            return Start((ulong)((random ?? Random.Shared).NextDouble() * ulong.MaxValue));
        }

        public static State Start(ulong x)
        {
            State state = default;

            // https://doi.org/10.1145/1276927.1276928 - apparently, initializing the state with a
            // similar generator can be problematic. it's very likely that Random instances violate
            // that rule, so use SplitMix64 (at the recommendation of the xoshiro256+ authors).
            state[0] = NextSplitMix64(ref x);
            state[1] = NextSplitMix64(ref x);
            state[2] = NextSplitMix64(ref x);
            state[3] = NextSplitMix64(ref x);
            return state;
        }

        public readonly bool IsValid => !((ReadOnlySpan<ulong>)this).SequenceEqual([0ul, 0ul, 0ul, 0ul]);

        public static bool operator ==(State first, State second) => first.Equals(second);

        public static bool operator !=(State first, State second) => !(first == second);

        public readonly bool Equals(State other)
        {
            return ((ReadOnlySpan<ulong>)this).SequenceEqual(other);
        }

        public readonly override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is State other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            HashCode h = default;
            h.AddBytes(MemoryMarshal.AsBytes((ReadOnlySpan<ulong>)this));
            return h.ToHashCode();
        }

        public sealed class Converter : JsonConverter<State>
        {
            // 43 payload, 1 padding
            private const byte Base64Length = 44;

            public override State Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                // reader MUST be validated, because we make very strong assumptions about data that
                // DOES NOT necessarily come from a fully trusted source.
                ValidateReader(in reader);

                if (reader.HasValueSequence)
                {
                    // this is very unlikely.
                    return UnlikelyDecode(reader.ValueSequence);
                }

                // either this is doable in one shot, the data is invalid, I've misunderstood how I
                // should be using these relatively new (to me) tools, or there's a bug. in order of
                // likelihood from most likely to least, I think.
                Unsafe.SkipInit(out State result);
                Span<byte> dst = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref result, 1));
                if (!(Base64.DecodeFromUtf8(reader.ValueSpan, dst, out int bytesConsumed, out int bytesWritten) == OperationStatus.Done && bytesConsumed == Base64Length && bytesWritten == dst.Length))
                {
                    ThrowJsonException();
                }

                return result;
            }

            public override void Write(Utf8JsonWriter writer, State value, JsonSerializerOptions options)
            {
                ReadOnlySpan<byte> src = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1));
                writer.WriteBase64StringValue(src);
            }

            private static void ValidateReader(in Utf8JsonReader reader)
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    ThrowJsonException();
                }

                long length = reader.HasValueSequence
                    ? reader.ValueSequence.Length
                    : reader.ValueSpan.Length;

                if (length != Base64Length)
                {
                    ThrowJsonException();
                }
            }

            [DoesNotReturn]
            private static void ThrowJsonException() => throw new JsonException();

            private static State UnlikelyDecode(ReadOnlySequence<byte> seq)
            {
                Span<byte> bytes = stackalloc byte[Base64Length];
                seq.CopyTo(bytes);
                if (!(Base64.DecodeFromUtf8InPlace(bytes, out int bytesWritten) == OperationStatus.Done && bytesWritten == Unsafe.SizeOf<State>()))
                {
                    ThrowJsonException();
                }

                return MemoryMarshal.Read<State>(bytes);
            }
        }
    }
}
