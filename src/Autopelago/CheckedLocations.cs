using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Autopelago;

public sealed record CheckedLocations
{
    private readonly Lazy<FrozenDictionary<string, BitArray>> _lookup;

    private readonly Lazy<ShortestPaths> _shortestPaths;

    public CheckedLocations()
    {
        _lookup = new(() =>
        {
            Dictionary<string, BitArray> lookup = [];
            foreach (LocationDefinitionModel loc in InCheckedOrder)
            {
                ref BitArray? bits = ref CollectionsMarshal.GetValueRefOrAddDefault(lookup, loc.Key.RegionKey, out _);
                (bits ??= new(loc.Region.Locations.Length, false))[loc.Key.N] = true;
            }

            return lookup.ToFrozenDictionary();
        });
        _shortestPaths = new(() => new(GameDefinitions.Instance, this));
    }

    public required ImmutableArray<LocationDefinitionModel> InCheckedOrder { get; init; }

    public int Count => InCheckedOrder.Length;

    public ShortestPaths ShortestPaths => _shortestPaths.Value;

    public bool Contains(LocationDefinitionModel location)
    {
        return _lookup.Value.TryGetValue(location.Key.RegionKey, out BitArray? bits) && bits[location.Key.N];
    }

    public bool Contains(LandmarkRegionDefinitionModel landmark)
    {
        return _lookup.Value.ContainsKey(landmark.Key);
    }

    public bool Equals(CheckedLocations? other)
    {
        return
            other is not null &&
            InCheckedOrder.SequenceEqual(other.InCheckedOrder);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            InCheckedOrder.Length
        );
    }
}
