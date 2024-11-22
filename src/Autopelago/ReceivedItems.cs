using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Autopelago;

public sealed record ReceivedItems
{
    private readonly Lazy<FrozenSet<ItemDefinitionModel>> _lookup;

    private readonly Lazy<int> _ratCount;

    private readonly Lazy<ShortestPaths> _shortestPaths;

    public ReceivedItems()
    {
        _lookup = new(() => [.. InReceivedOrder!]);
        _ratCount = new(() => InReceivedOrder!.Sum(i => i.RatCount.GetValueOrDefault()));
        _shortestPaths = new(() => new(GameDefinitions.Instance, this));
    }

    public required ImmutableList<ItemDefinitionModel> InReceivedOrder { get; init; }

    public int Count => InReceivedOrder.Count;

    public ShortestPaths ShortestPaths => _shortestPaths.Value;

    public bool Contains(ItemDefinitionModel item) => _lookup.Value.Contains(item);

    public int RatCount => _ratCount.Value;

    public bool Equals(ReceivedItems? other)
    {
        return
            other is not null &&
            InReceivedOrder.SequenceEqual(other.InReceivedOrder);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            InReceivedOrder.Count
        );
    }
}
