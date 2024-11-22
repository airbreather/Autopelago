using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Autopelago;

public sealed record CheckedLocations
{
    public required ImmutableArray<LocationDefinitionModel> InCheckedOrder
    {
        get;
        init
        {
            field = value;
            AsFrozenSet = [.. value];
        }
    }

    public int Count => InCheckedOrder.Length;

    public FrozenSet<LocationDefinitionModel> AsFrozenSet { get; private init; } = [];

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
