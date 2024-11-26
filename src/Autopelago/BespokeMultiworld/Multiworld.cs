using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Autopelago.BespokeMultiworld;

public sealed class Multiworld
{
    public required ImmutableArray<World> Slots { get; init; }

    public required FrozenDictionary<WorldLocation, WorldItem> FullSpoilerData { get; init; }
}
