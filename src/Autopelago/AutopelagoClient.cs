using System.Collections.Immutable;

using ArchipelagoClientDotNet;

namespace Autopelago;

public abstract class AutopelagoClientFactory
{
    public abstract ValueTask<AutopelagoClient> CreateClientAsync(CancellationToken cancellationToken);
}

public abstract class AutopelagoClient
{
    public abstract event AsyncEventHandler<ReceivedItemsEventArgs> ReceivedItems;

    public abstract ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken);
}

public sealed record ReceivedItemsEventArgs
{
    public required int Index { get; init; }

    public required ImmutableArray<ItemDefinitionModel> Items { get; init; }
}
