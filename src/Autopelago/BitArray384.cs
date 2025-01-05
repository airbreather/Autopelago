using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Autopelago;

// 384 bits = 48 bytes = 6 ulong values.
[StructLayout(LayoutKind.Auto)]
[DebuggerTypeProxy(typeof(BitArray384DebugView))]
public struct BitArray384 : IEquatable<BitArray384>
{
    public static readonly BitArray384 AllFalse = new(384, false);

    public static readonly BitArray384 AllTrue = new(384, true);

    private readonly ushort _length;

    private Bits384 _bits;

    public BitArray384(int length, bool defaultValue = false)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)length, 384U);
        _length = (ushort)length;
        if (defaultValue)
        {
            SetAll(true);
        }
        else
        {
            _bits = default;
        }
    }

    public readonly int Length => _length;

    public bool this[int index]
    {
        readonly get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, _length);
            return (_bits[index >> 6] & (1UL << index)) != 0;
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, _length);
            ref ulong slot = ref _bits[index >> 6];
            slot = value
                ? slot | (1UL << index)
                : slot & ~(1UL << index);
        }
    }

    public readonly bool HasAllSet
    {
        get
        {
            int rem;
            return
                !_bits[..(_length >> 6)].ContainsAnyExcept(ulong.MaxValue) &&
                ((rem = _length & ((1 << 6) - 1)) == 0 || BitOperations.PopCount(_bits[_length >> 6]) == rem);
        }
    }

    public readonly bool HasAnySet => ((ReadOnlySpan<ulong>)_bits).ContainsAnyExcept(0UL);

    public readonly int TrueCount
    {
        get
        {
            ReadOnlySpan<ulong> bits = _bits;
            int result = 0;
            foreach (ulong val in bits)
            {
                result += BitOperations.PopCount(val);
            }

            return result;
        }
    }

    public readonly int FalseCount => Length - TrueCount;

    public void SetAll(bool value)
    {
        if (value)
        {
            _bits[..(_length >> 6)].Fill(ulong.MaxValue);
            if ((_length & ((1 << 6) - 1)) is int rem and not 0)
            {
                _bits[_length >> 6] = ((1UL << rem) - 1);
            }
        }
        else
        {
            _bits = default;
        }
    }

    public void Clear()
    {
        _bits = default;
    }

    public readonly bool Equals(BitArray384 other)
    {
        ReadOnlySpan<ulong> span1 = this._bits;
        ReadOnlySpan<ulong> span2 = other._bits;
        return span1.SequenceEqual(span2);
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return
            obj is BitArray384 other &&
            Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Length,
            _bits[0],
            _bits[1],
            _bits[2],
            _bits[3],
            _bits[4],
            _bits[5]
        );
    }

    [InlineArray(384 >> 6)]
    private struct Bits384
    {
        // https://github.com/dotnet/roslyn/issues/71500
#pragma warning disable IDE0044 // Add readonly modifier
#pragma warning disable IDE0051 // Remove unused private members
        private ulong _field;
#pragma warning restore IDE0051
#pragma warning restore IDE0044
    }

    private sealed class BitArray384DebugView
    {
        public BitArray384DebugView(BitArray384 bits)
        {
            Bits = new(bits.Length);
            for (int i = 0; i < bits.Length; i++)
            {
                Bits[i] = bits[i];
            }
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public BitArray Bits { get; }
    }
}
