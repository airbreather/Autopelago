using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Autopelago;

public sealed record CheckedLocations
{
    private readonly Dictionary<string, SmallBitArray> _bitmap =
        GameDefinitions.Instance.AllRegions
            .ToDictionary(r => r.Key, r => new SmallBitArray(r.Value.Locations.Length));

    private readonly List<LocationDefinitionModel> _order = [];

    public CheckedLocations()
    {
        Order = _order.AsReadOnly();
    }

    public ReadOnlyCollection<LocationDefinitionModel> Order { get; }

    public int Count => _order.Count;

    public bool this[LocationKey location]
    {
        get => _bitmap[location.RegionKey][location.N];
    }

    public bool this[LocationDefinitionModel location]
    {
        get => _bitmap[location.Key.RegionKey][location.Key.N];
    }

    public SmallBitArray this[string regionKey]
    {
        get => _bitmap[regionKey];
    }

    public bool MarkChecked(LocationDefinitionModel location)
    {
        LocationKey key = location.Key;
        ref SmallBitArray bitmap = ref CollectionsMarshal.GetValueRefOrNullRef(_bitmap, key.RegionKey);
        if (Unsafe.IsNullRef(ref bitmap) || bitmap[key.N])
        {
            return false;
        }

        _order.Add(location);
        return bitmap[key.N] = true;
    }

    public bool Equals(CheckedLocations? other)
    {
        return
            other is not null &&
            _order.SequenceEqual(other._order);
    }

    public override int GetHashCode()
    {
        HashCode hc = default;
        hc.Add(_order.Count);
        foreach (ref readonly LocationDefinitionModel location in CollectionsMarshal.AsSpan(_order))
        {
            hc.Add(location.Key.RegionKey.Length);
            hc.Add(location.Key);
        }

        return hc.ToHashCode();
    }
}
