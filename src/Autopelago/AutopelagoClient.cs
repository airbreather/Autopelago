using System.Collections.Immutable;

namespace Autopelago;

public abstract class AutopelagoClient
{
    public abstract event AsyncEventHandler<ReceivedItemsEventArgs> ReceivedItems;

    public abstract ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken);

    public abstract ValueTask SendMessageAsync(string message, CancellationToken cancellationToken);

    public abstract ValueTask IWonAsync(CancellationToken cancellationToken);
}

public sealed record ReceivedItemsEventArgs
{
    public required int Index { get; init; }

    public required ImmutableArray<ItemDefinitionModel> Items { get; init; }
}
