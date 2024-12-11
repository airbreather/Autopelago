using System.Collections;

namespace Autopelago;

public sealed class ReadOnlyBitArray : IReadOnlyList<bool>
{
    private readonly BitArray _array;

    public ReadOnlyBitArray(BitArray array)
    {
        _array = array;
    }

    public int Count => _array.Length;

    public bool HasAllSet => _array.HasAllSet();

    public bool HasAnySet => _array.HasAnySet();

    public bool this[int index] => _array[index];

    public Enumerator GetEnumerator()
    {
        return new(this);
    }

    IEnumerator<bool> IEnumerable<bool>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public struct Enumerator : IEnumerator<bool>
    {
        private readonly ReadOnlyBitArray _array;

        private int _index;

        internal Enumerator(ReadOnlyBitArray array)
        {
            _array = array;
            _index = -1;
        }

        public readonly bool Current => _array[_index];

        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => ++_index < _array.Count;

        public void Reset() => _index = -1;

        public readonly void Dispose() { }
    }
}
