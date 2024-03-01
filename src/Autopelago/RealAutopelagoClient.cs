using System.Collections.Frozen;
using System.Collections.Immutable;

using ArchipelagoClientDotNet;

public sealed class RealAutopelagoClient : IAutopelagoClient
{
    private readonly IArchipelagoClient _client;

    private readonly AsyncEvent<ReceivedItemsEventArgs> _receivedItemsEvent = new();

    private FrozenDictionary<long, ItemDefinitionModel>? _itemsMapping;

    private FrozenDictionary<LocationDefinitionModel, long>? _locationsMapping;

    public RealAutopelagoClient(IArchipelagoClient client)
    {
        _client = client;
        _client.PacketReceived += OnEarlyPacketReceivedAsync;
    }

    public event AsyncEventHandler<ReceivedItemsEventArgs> ReceivedItems
    {
        add => _receivedItemsEvent.Add(value);
        remove => _receivedItemsEvent.Remove(value);
    }

    public async ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        LocationChecksPacketModel packet = new()
        {
            Locations = locations.Select(l => _locationsMapping![l]).ToImmutableArray().AsMemory(),
        };

        await _client.WriteNextPacketAsync(packet, cancellationToken);
    }

    private ValueTask OnEarlyPacketReceivedAsync(object? sender, PacketReceivedEventArgs args, CancellationToken cancellationToken)
    {
        if (args.Packet is not DataPackagePacketModel dataPackage)
        {
            return ValueTask.CompletedTask;
        }

        _client.PacketReceived -= OnEarlyPacketReceivedAsync;
        _client.PacketReceived += OnNormalPacketReceivedAsync;
        GameDataModel gameData = dataPackage.Data.Games["Autopelago"];
        _itemsMapping = gameData.ItemNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.ItemsByName[kvp.Key]);
        _locationsMapping = gameData.LocationNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.LocationsByName[kvp.Key], kvp => kvp.Value);
        return ValueTask.CompletedTask;
    }

    private async ValueTask OnNormalPacketReceivedAsync(object? sender, PacketReceivedEventArgs args, CancellationToken cancellationToken)
    {
        if (args.Packet is not ReceivedItemsPacketModel receivedItems)
        {
            return;
        }

        await Helper.ConfigureAwaitFalse();
        ReceivedItemsEventArgs newArgs = new()
        {
            Index = receivedItems.Index,
            Items = [.. receivedItems.Items.Select(i => _itemsMapping![i.Item])],
        };

        await _receivedItemsEvent.InvokeAsync(this, newArgs, cancellationToken);
    }
}
