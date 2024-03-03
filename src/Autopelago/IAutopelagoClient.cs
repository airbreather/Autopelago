using System.Collections.Immutable;

using ArchipelagoClientDotNet;

namespace Autopelago;

public sealed record ReceivedItemsEventArgs
{
    public required int Index { get; init; }

    public required ImmutableArray<ItemDefinitionModel> Items { get; init; }
}

public interface IAutopelagoClient
{
    event AsyncEventHandler<ReceivedItemsEventArgs> ReceivedItems;

    ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken);
}
