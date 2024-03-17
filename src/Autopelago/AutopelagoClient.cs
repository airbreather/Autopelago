using System.Collections.Immutable;

namespace Autopelago;

public abstract class AutopelagoClient
{
    public abstract IObservable<ReceivedItemsEventArgs> ReceivedItemsEvents { get; }

    public abstract ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken);
}

public sealed record ReceivedItemsEventArgs
{
    public required int Index { get; init; }

    public required ImmutableArray<ItemDefinitionModel> Items { get; init; }
}
