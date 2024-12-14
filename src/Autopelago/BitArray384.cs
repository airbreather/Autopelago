using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Autopelago;

// 384 bits = 48 bytes = 6 ulong values.
[StructLayout(LayoutKind.Auto)]
public struct BitArray384
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
}
