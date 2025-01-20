using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Autopelago;

[StructLayout(LayoutKind.Auto, Pack = 1)]
[DebuggerTypeProxy(typeof(BitArray128DebugView))]
public struct BitArray128 : IEquatable<BitArray128>
{
    private readonly byte _length;

    private UInt128 _bits;

    public BitArray128(int length)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)length, 128u);
        _length = (byte)length;
    }

    public readonly int Length => _length;

    public bool this[int index]
    {
        readonly get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, _length);
            return (_bits & (UInt128.One << index)) != UInt128.Zero;
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, _length);
            _bits = value
                ? (_bits | (UInt128.One << index))
                : (_bits & ~(UInt128.One << index));
        }
    }

    public readonly bool HasAllSet
    {
        get => _bits == ((UInt128.One << _length) - UInt128.One);
    }

    public readonly bool HasAnySet
    {
        get => _bits != UInt128.Zero;
    }

    public readonly int TrueCount => (int)UInt128.PopCount(_bits);

    public readonly int FalseCount => Length - TrueCount;

    public void SetAll(bool value)
    {
        _bits = value
            ? (UInt128.One << _length) - UInt128.One
            : UInt128.Zero;
    }

    public void Clear()
    {
        _bits = UInt128.Zero;
    }

    public static bool operator ==(BitArray128 left, BitArray128 right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BitArray128 left, BitArray128 right)
    {
        return !(left == right);
    }

    public readonly bool Equals(BitArray128 other)
    {
        return
            Length == other.Length &&
            _bits == other._bits;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return
            obj is BitArray128 other &&
            Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Length,
            _bits
        );
    }

    private sealed class BitArray128DebugView
    {
        public BitArray128DebugView(BitArray128 bits)
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
