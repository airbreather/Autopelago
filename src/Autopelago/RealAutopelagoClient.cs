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
    private readonly ArchipelagoConnection _connection;

    private readonly IConnectableObservable<AutopelagoData> _serverGameData;

    public RealAutopelagoClient(ArchipelagoConnection connection)
    {
        _connection = connection;
        _serverGameData = Observable
            .CombineLatest(
                connection.IncomingPackets.OfType<DataPackagePacketModel>(),
                connection.IncomingPackets.OfType<ConnectedPacketModel>(),
                connection.IncomingPackets.OfType<RoomUpdatePacketModel>().StartWith(default(RoomUpdatePacketModel)),
                (dataPackage, connected, roomUpdate) =>
                {
                    GameDataModel gameData = dataPackage.Data.Games["Autopelago"];
                    return new AutopelagoData()
                    {
                        TeamNumber = connected.Team,
                        SlotNumber = connected.Slot,
                        SlotInfo = (roomUpdate?.SlotInfo ?? connected.SlotInfo).ToFrozenDictionary(),
                        InitialSlotData = connected.SlotData.ToFrozenDictionary(),
                        ItemsMapping = gameData.ItemNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.ItemsByName[kvp.Key], kvp => kvp.Value),
                        LocationsMapping = gameData.LocationNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.LocationsByName[kvp.Key], kvp => kvp.Value),
                        ItemsReverseMapping = gameData.ItemNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.ItemsByName[kvp.Key]),
                        LocationsReverseMapping = gameData.LocationNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.LocationsByName[kvp.Key]),
                    };
                })
            .Replay(1);

        ReceivedItemsEvents = connection.IncomingPackets.OfType<ReceivedItemsPacketModel>()
            .WithLatestFrom(_serverGameData)
            .Select(
                tup =>
                {
                    (ReceivedItemsPacketModel receivedItems, AutopelagoData mappings) = tup;
                    return new ReceivedItemsEventArgs
                    {
                        Index = receivedItems.Index,
                        Items = [.. receivedItems.Items.Select(i => mappings.ItemsReverseMapping[i.Item])],
                    };
                });

        _serverGameData.Connect();
    }

    public override IObservable<ReceivedItemsEventArgs> ReceivedItemsEvents { get; }

    public IObservable<AutopelagoData> ServerGameData => _serverGameData;

    public override async ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        AutopelagoData mappings = await _serverGameData.FirstAsync().ToTask(cancellationToken);
        LocationChecksPacketModel packet = new()
        {
            Locations = locations.Select(l => mappings.LocationsMapping[l]).ToImmutableArray().AsMemory(),
        };

        await _connection.SendPacketsAsync([packet], cancellationToken);
    }

    public async ValueTask<Game.State?> InitAsync(GetDataPackagePacketModel getDataPackage, ConnectPacketModel connect, Random? random, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        ConnectResponsePacketModel connectResponse = await _connection.HandshakeAsync(_ => getDataPackage, (_, _) => connect, cancellationToken);
        if (connectResponse is not ConnectedPacketModel connected)
        {
            return null;
        }

        AutopelagoData data = await _serverGameData.FirstAsync().ToTask(cancellationToken);
        string stateKey = GetStateKey(data);
        Game.State? state = null;
        GetPacketModel get = new() { Keys = [stateKey] };
        Task<RetrievedPacketModel> retrievedTask = _connection.IncomingPackets.OfType<RetrievedPacketModel>().FirstAsync().ToTask(cancellationToken);
        await _connection.SendPacketsAsync([get], cancellationToken);
        RetrievedPacketModel retrieved = await retrievedTask;
        if (retrieved.Keys.TryGetValue(stateKey, out JsonElement json) &&
            JsonSerializer.Deserialize<Game.State.Proxy>(json, Game.State.Proxy.SerializerOptions) is Game.State.Proxy proxy)
        {
            state = proxy.ToState();
        }

        state ??= Game.State.Start(random);

        if (!connected.CheckedLocations.IsEmpty)
        {
            state = state with { CheckedLocations = [.. connected.CheckedLocations.Select(l => data.LocationsReverseMapping[l])] };
        }

        return state;
    }

    public async ValueTask SaveGameStateAsync(Game.State state, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        AutopelagoData data = await _serverGameData.FirstAsync().ToTask(cancellationToken);
        DataStorageOperationModel op = new()
        {
            Operation = ArchipelagoDataStorageOperationType.Replace,
            Value = JsonSerializer.SerializeToNode(state.ToProxy(), Game.State.Proxy.SerializerOptions)!,
        };

        SetPacketModel set = new()
        {
            Key = GetStateKey(data),
            Operations = [op],
        };

        await _connection.SendPacketsAsync([set], cancellationToken);
    }

    private static string GetStateKey(AutopelagoData data)
    {
        return $"autopelago_state_{data.TeamNumber}_{data.SlotNumber}";
    }

    public sealed record AutopelagoData
    {
        public required int TeamNumber { get; init; }

        public required int SlotNumber { get; init; }

        public required FrozenDictionary<int, SlotModel> SlotInfo { get; init; }

        public required FrozenDictionary<string, JsonElement> InitialSlotData { get; init; }

        public required FrozenDictionary<ItemDefinitionModel, long> ItemsMapping { get; init; }

        public required FrozenDictionary<LocationDefinitionModel, long> LocationsMapping { get; init; }

        public required FrozenDictionary<long, ItemDefinitionModel> ItemsReverseMapping { get; init; }

        public required FrozenDictionary<long, LocationDefinitionModel> LocationsReverseMapping { get; init; }
    }
}
