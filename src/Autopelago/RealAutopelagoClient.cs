using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text.Json;

using ArchipelagoClientDotNet;

namespace Autopelago;

public sealed class RealAutopelagoClient : AutopelagoClient
{
    private readonly IArchipelagoConnection _connection;

    private readonly IConnectableObservable<Mappings> _mappings;

    public RealAutopelagoClient(IArchipelagoConnection connection)
    {
        _connection = connection;
        _mappings = connection.IncomingPackets
            .OfType<DataPackagePacketModel>()
            .Select(dataPackage =>
            {
                GameDataModel gameData = dataPackage.Data.Games["Autopelago"];
                return new Mappings()
                {
                    ItemsMapping = gameData.ItemNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.ItemsByName[kvp.Key], kvp => kvp.Value),
                    LocationsMapping = gameData.LocationNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.LocationsByName[kvp.Key], kvp => kvp.Value),
                    ItemsReverseMapping = gameData.ItemNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.ItemsByName[kvp.Key]),
                    LocationsReverseMapping = gameData.LocationNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.LocationsByName[kvp.Key]),
                };
            })
            .Replay(1);

        ReceivedItemsEvents = _mappings
            .CombineLatest(connection.IncomingPackets.OfType<ReceivedItemsPacketModel>(), (dataPackage, receivedItems) => new ReceivedItemsEventArgs
            {
                Index = receivedItems.Index,
                Items = [.. receivedItems.Items.Select(i => dataPackage.ItemsReverseMapping[i.Item])],
            });

        _mappings.Connect();
    }

    public override IObservable<ReceivedItemsEventArgs> ReceivedItemsEvents { get; }

    public override async ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        Mappings mappings = await _mappings.FirstAsync().ToTask(cancellationToken);
        LocationChecksPacketModel packet = new()
        {
            Locations = locations.Select(l => mappings.LocationsMapping[l]).ToImmutableArray().AsMemory(),
        };

        await _connection.SendPacketsAsync([packet], cancellationToken);
    }

    public async ValueTask<Game.State> InitGameStateAsync(ConnectedPacketModel connected, Random? random, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        Mappings mappings = await _mappings.FirstAsync().ToTask(cancellationToken);
        string stateKey = GetStateKey(connected);
        GetPacketModel get = new() { Keys = [stateKey] };
        Task<RetrievedPacketModel> retrievedTask = _connection.IncomingPackets.OfType<RetrievedPacketModel>().FirstAsync().ToTask(cancellationToken);
        await _connection.SendPacketsAsync([get], cancellationToken);
        RetrievedPacketModel retrieved = await retrievedTask;

        Game.State? state = null;
        if (retrieved.Keys.TryGetValue(stateKey, out JsonElement json) &&
            JsonSerializer.Deserialize<Game.State.Proxy>(json, Game.State.Proxy.SerializerOptions) is Game.State.Proxy proxy)
        {
            state = proxy.ToState();
        }

        state ??= Game.State.Start(random);

        if (!connected.CheckedLocations.IsEmpty)
        {
            state = state with { CheckedLocations = [.. connected.CheckedLocations.Select(l => mappings.LocationsReverseMapping[l])] };
        }

        return state;
    }

    public async ValueTask SaveGameStateAsync(ConnectedPacketModel connected, Game.State state, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        DataStorageOperationModel op = new()
        {
            Operation = ArchipelagoDataStorageOperationType.Replace,
            Value = JsonSerializer.SerializeToNode(state.ToProxy(), Game.State.Proxy.SerializerOptions)!,
        };

        SetPacketModel set = new()
        {
            Key = GetStateKey(connected),
            Operations = [op],
        };

        await _connection.SendPacketsAsync([set], cancellationToken);
    }

    private static string GetStateKey(ConnectedPacketModel connected)
    {
        return $"autopelago_state_{connected.Team}_{connected.Slot}";
    }

    private sealed record Mappings
    {
        public required FrozenDictionary<ItemDefinitionModel, long> ItemsMapping { get; init; }

        public required FrozenDictionary<LocationDefinitionModel, long> LocationsMapping { get; init; }

        public required FrozenDictionary<long, ItemDefinitionModel> ItemsReverseMapping { get; init; }

        public required FrozenDictionary<long, LocationDefinitionModel> LocationsReverseMapping { get; init; }
    }
}
