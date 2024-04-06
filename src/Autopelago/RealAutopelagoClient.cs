using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;

namespace Autopelago;

public sealed class RealAutopelagoClient : AutopelagoClient
{
    private readonly AsyncEvent<ReceivedItemsEventArgs> _receivedItemsEvent = new();

    private readonly AsyncEvent<AutopelagoData> _serverGameDataEvent = new();

    private readonly ArchipelagoConnection _connection;

    private AutopelagoData? _lastServerGameData;

    public RealAutopelagoClient(ArchipelagoConnection connection)
    {
        _connection = connection;

        DataPackagePacketModel? lastDataPackage = null;
        ConnectedPacketModel? lastConnected = null;
        RoomUpdatePacketModel? lastRoomUpdate = null;
        connection.IncomingPacket += OnIncomingPacketAsync;
        ValueTask OnIncomingPacketAsync(object? sender, ArchipelagoPacketModel packet, CancellationToken cancellationToken)
        {
            bool updateServerGameData = false;
            ReceivedItemsPacketModel? currentReceivedItems = null;
            switch (packet)
            {
                case DataPackagePacketModel dataPackage:
                    lastDataPackage = dataPackage;
                    updateServerGameData = true;
                    break;

                case ConnectedPacketModel connected:
                    lastConnected = connected;
                    updateServerGameData = true;
                    break;

                case RoomUpdatePacketModel roomUpdate:
                    lastRoomUpdate = roomUpdate;
                    updateServerGameData = true;
                    break;

                case ReceivedItemsPacketModel receivedItems:
                    currentReceivedItems = receivedItems;
                    break;
            }

            if (updateServerGameData && lastDataPackage != null && lastConnected != null)
            {
                GameDataModel gameData = lastDataPackage.Data.Games["Autopelago"];
                _lastServerGameData = new()
                {
                    TeamNumber = lastConnected.Team,
                    SlotNumber = lastConnected.Slot,
                    SlotInfo = (lastRoomUpdate?.SlotInfo ?? lastConnected.SlotInfo).ToFrozenDictionary(),
                    InitialSlotData = lastConnected.SlotData.ToFrozenDictionary(),
                    GeneralItemNameMapping = lastDataPackage.Data.Games.SelectMany(game => game.Value.ItemNameToId).ToFrozenDictionary(kvp => kvp.Value, kvp => kvp.Key),
                    GeneralLocationNameMapping = lastDataPackage.Data.Games.SelectMany(game => game.Value.LocationNameToId).ToFrozenDictionary(kvp => kvp.Value, kvp => kvp.Key),
                    ItemsMapping = gameData.ItemNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.ItemsByName[kvp.Key], kvp => kvp.Value),
                    LocationsMapping = gameData.LocationNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.LocationsByName[kvp.Key], kvp => kvp.Value),
                    ItemsReverseMapping = gameData.ItemNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.ItemsByName[kvp.Key]),
                    LocationsReverseMapping = gameData.LocationNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.LocationsByName[kvp.Key]),
                };

                return _serverGameDataEvent.InvokeAsync(this, _lastServerGameData, cancellationToken);
            }

            if (currentReceivedItems is not null && _lastServerGameData is not null)
            {
                ReceivedItemsEventArgs args = new()
                {
                    Index = currentReceivedItems.Index,
                    Items = [.. currentReceivedItems.Items.Select(i => _lastServerGameData.ItemsReverseMapping[i.Item])],
                };
                return _receivedItemsEvent.InvokeAsync(this, args, cancellationToken);
            }

            return ValueTask.CompletedTask;
        }
    }

    public override event AsyncEventHandler<ReceivedItemsEventArgs> ReceivedItems
    {
        add { _receivedItemsEvent.Add(value); }
        remove { _receivedItemsEvent.Remove(value); }
    }

    public AutopelagoData? LastServerGameData => _lastServerGameData;

    public event AsyncEventHandler<AutopelagoData> ServerGameData
    {
        add { _serverGameDataEvent.Add(value); }
        remove { _serverGameDataEvent.Remove(value); }
    }

    public override async ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        AutopelagoData serverGameData = await CurrentOrNextServerGameDataAsync(cancellationToken);
        LocationChecksPacketModel packet = new()
        {
            Locations = locations.Select(l => serverGameData.LocationsMapping[l]).ToImmutableArray().AsMemory(),
        };

        await _connection.SendPacketsAsync([packet], cancellationToken);
    }

    public override async ValueTask SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        SayPacketModel say = new()
        {
            Text = message,
        };
        await _connection.SendPacketsAsync([say], cancellationToken);
    }

    public async ValueTask IWonAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        StatusUpdatePacketModel statusUpdate = new()
        {
            Status = ArchipelagoClientStatus.Goal,
        };
        await _connection.SendPacketsAsync([statusUpdate], cancellationToken);
    }

    public async ValueTask<Game.State?> InitAsync(GetDataPackagePacketModel getDataPackage, ConnectPacketModel connect, Random? random, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        ConnectResponsePacketModel connectResponse = await _connection.HandshakeAsync(_ => getDataPackage, (_, _) => connect, cancellationToken);
        if (connectResponse is not ConnectedPacketModel connected)
        {
            return null;
        }

        AutopelagoData data = await CurrentOrNextServerGameDataAsync(cancellationToken);
        string stateKey = GetStateKey(data);
        Game.State? state = null;
        GetPacketModel get = new() { Keys = [stateKey] };
        ValueTask<RetrievedPacketModel> retrievedTask = Helper.NextAsync(
            subscribe: e => _connection.IncomingPacket += e,
            unsubscribe: e => _connection.IncomingPacket -= e,
            predicate: p => p is RetrievedPacketModel,
            selector: (ArchipelagoPacketModel p) => (RetrievedPacketModel)p,
            cancellationToken: cancellationToken);
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
        AutopelagoData data = await CurrentOrNextServerGameDataAsync(cancellationToken);
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

    private ValueTask<AutopelagoData> CurrentOrNextServerGameDataAsync(CancellationToken cancellationToken)
    {
        return _lastServerGameData is AutopelagoData serverGameData
            ? ValueTask.FromResult(serverGameData)
            : CurrentOrNextServerGameDataRareAsync(cancellationToken);
    }

    private async ValueTask<AutopelagoData> CurrentOrNextServerGameDataRareAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ValueTask<AutopelagoData> next = Helper.NextAsync(
            subscribe: e => ServerGameData += e,
            unsubscribe: e => ServerGameData -= e,
            predicate: _ => true,
            selector: (AutopelagoData d) => d,
            cancellationToken: cts.Token
        );

        // check if the data came in between when we last checked and when we just subscribed.
        if (_lastServerGameData is AutopelagoData serverGameData)
        {
            await cts.CancelAsync();
            return serverGameData;
        }

        return await next;
    }

    public sealed record AutopelagoData
    {
        public required int TeamNumber { get; init; }

        public required int SlotNumber { get; init; }

        public required FrozenDictionary<long, string> GeneralItemNameMapping { get; init; }

        public required FrozenDictionary<long, string> GeneralLocationNameMapping { get; init; }

        public required FrozenDictionary<int, SlotModel> SlotInfo { get; init; }

        public required FrozenDictionary<string, JsonElement> InitialSlotData { get; init; }

        public required FrozenDictionary<ItemDefinitionModel, long> ItemsMapping { get; init; }

        public required FrozenDictionary<LocationDefinitionModel, long> LocationsMapping { get; init; }

        public required FrozenDictionary<long, ItemDefinitionModel> ItemsReverseMapping { get; init; }

        public required FrozenDictionary<long, LocationDefinitionModel> LocationsReverseMapping { get; init; }
    }
}
