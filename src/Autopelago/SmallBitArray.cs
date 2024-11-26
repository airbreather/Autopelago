using System.Runtime.InteropServices;

namespace Autopelago;

[StructLayout(LayoutKind.Auto, Pack = 0)]
public struct SmallBitArray
{
    private readonly byte _length;

    private UInt128 _bits;

    public SmallBitArray(int length)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)length, 127u);
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
}
