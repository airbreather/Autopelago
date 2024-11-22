using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Autopelago;

public sealed record ReceivedItems
{
    public GameDefinitions GameDefinitions => GameDefinitions.Instance;

    public required ImmutableList<ItemDefinitionModel> InReceivedOrder
    {
        get;
        init
        {
            field = value;
            AsFrozenSet = [.. value];
            RatCount = value.Sum(v => v.RatCount.GetValueOrDefault());
        }
    }

    public int Count => InReceivedOrder.Count;

    public FrozenSet<ItemDefinitionModel> AsFrozenSet { get; private init; } = [];

    public int RatCount { get; private init; }

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
