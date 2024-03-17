using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reactive.Linq;

using ArchipelagoClientDotNet;

namespace Autopelago;

public sealed class RealAutopelagoClient : AutopelagoClient
{
    private readonly IArchipelagoConnection _connection;

    private readonly Task<FrozenDictionary<LocationDefinitionModel, long>> _locationsMapping;

    public RealAutopelagoClient(IArchipelagoConnection connection)
    {
        _connection = connection;
        TaskCompletionSource<FrozenDictionary<LocationDefinitionModel, long>> locationsMappingBox = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _locationsMapping = locationsMappingBox.Task;

        ReceivedItemsEvents = connection.IncomingPackets
            .OfType<DataPackagePacketModel>()
            .Select(dataPackage =>
            {
                GameDataModel gameData = dataPackage.Data.Games["Autopelago"];
                return new
                {
                    ItemsMapping = gameData.ItemNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.ItemsByName[kvp.Key]),
                    LocationsMapping = gameData.LocationNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.LocationsByName[kvp.Key], kvp => kvp.Value),
                };
            })
            .FirstAsync()
            .Do(
                val => locationsMappingBox.TrySetResult(val.LocationsMapping),
                err => locationsMappingBox.TrySetException(err)
            )
            .CombineLatest(connection.IncomingPackets.OfType<ReceivedItemsPacketModel>(), (dataPackage, receivedItems) => new ReceivedItemsEventArgs
            {
                Index = receivedItems.Index,
                Items = [.. receivedItems.Items.Select(i => dataPackage.ItemsMapping[i.Item])],
            });
    }

    public override IObservable<ReceivedItemsEventArgs> ReceivedItemsEvents { get; }

    public override async ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        FrozenDictionary<LocationDefinitionModel, long> locationsMapping = await _locationsMapping.WaitAsync(cancellationToken);
        LocationChecksPacketModel packet = new()
        {
            Locations = locations.Select(l => locationsMapping[l]).ToImmutableArray().AsMemory(),
        };

        await _connection.SendPacketsAsync([packet], cancellationToken);
    }
}
