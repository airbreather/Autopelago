using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Serilog;
using Serilog.Context;

namespace Autopelago;

public sealed class GameAndContext
{
    public required Game Game { get; init; }

    public required MultiworldInfo Context { get; init; }
}

public sealed record AutopelagoWorldMetadata
{
    public required string VersionStamp { get; init; }

    public required string VictoryLocationName { get; init; }

    public required ImmutableArray<BuffTokens> EnabledBuffs { get; init; }

    public required ImmutableArray<TrapTokens> EnabledTraps { get; init; }

    [JsonPropertyName("msg_changed_target")]
    public required ImmutableArray<WeightedString> ChangedTargetMessages { get; init; }

    [JsonPropertyName("msg_enter_go_mode")]
    public required ImmutableArray<WeightedString> EnteredGoModeMessages { get; init; }

    [JsonPropertyName("msg_enter_bk")]
    public required ImmutableArray<WeightedString> EnterBKMessages { get; init; }

    [JsonPropertyName("msg_remind_bk")]
    public required ImmutableArray<WeightedString> RemindBKMessages { get; init; }

    [JsonPropertyName("msg_exit_bk")]
    public required ImmutableArray<WeightedString> ExitBKMessages { get; init; }

    [JsonPropertyName("msg_completed_goal")]
    public required ImmutableArray<WeightedString> CompletedGoalMessages { get; init; }

    [JsonPropertyName("lactose_intolerant")]
    public required bool LactoseIntolerant { get; init; }
}

[JsonConverter(typeof(WeightedStringConverter))]
public sealed record WeightedString : IWeighted
{
    public required string Message { get; init; }

    public required int Weight { get; init; }

    internal sealed class WeightedStringConverter : JsonConverter<WeightedString>
    {
        public override WeightedString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (!(reader.TokenType == JsonTokenType.StartArray &&
                  reader.Read() && reader.TokenType == JsonTokenType.String && reader.GetString() is string message &&
                  reader.Read() && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int weight) &&
                  reader.Read() && reader.TokenType == JsonTokenType.EndArray))
            {
                throw new JsonException();
            }

            return new()
            {
                Message = message,
                Weight = weight,
            };
        }

        public override void Write(Utf8JsonWriter writer, WeightedString value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(value.Message);
            writer.WriteNumberValue(value.Weight);
            writer.WriteEndArray();
        }
    }
}

public sealed record MultiworldInfo
{
    public required int TeamNumber { get; init; }

    public required int SlotNumber { get; init; }

    public required FrozenDictionary<string, FrozenDictionary<long, string>> GeneralItemNameMapping { get; init; }

    public required FrozenDictionary<string, FrozenDictionary<long, string>> GeneralLocationNameMapping { get; init; }

    public required FrozenDictionary<int, SlotModel> SlotInfo { get; init; }

    public required ImmutableArray<long> LocationIds { get; init; }

    public required FrozenDictionary<long, LocationKey> LocationsById { get; init; }

    public required FrozenDictionary<long, ItemKey> ItemsById { get; init; }

    public required FrozenDictionary<int, string> PlayerAliasBySlot { get; init; }

    public required FrozenDictionary<string, int> SlotByPlayerAlias { get; init; }

    public required WeightedRandomItems<WeightedString> ChangedTargetMessages { get; init; }

    public required WeightedRandomItems<WeightedString> EnteredGoModeMessages { get; init; }

    public required WeightedRandomItems<WeightedString> EnterBKMessages { get; init; }

    public required WeightedRandomItems<WeightedString> RemindBKMessages { get; init; }

    public required WeightedRandomItems<WeightedString> ExitBKMessages { get; init; }

    public required WeightedRandomItems<WeightedString> CompletedGoalMessages { get; init; }

    public string ServerSavedStateKey => GetServerSavedStateKey(teamNumber: TeamNumber, slotNumber: SlotNumber);

    public static string GetServerSavedStateKey(int teamNumber, int slotNumber)
    {
        return $"autopelago_{teamNumber}_{slotNumber}";
    }
}

[JsonSerializable(typeof(ServerSavedState))]
[JsonSerializable(typeof(JsonElement))]
public sealed partial class ServerSavedStateSerializationContext : JsonSerializerContext;

[JsonSerializable(typeof(AutopelagoWorldMetadata))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public sealed partial class AutopelagoWorldMetadataSerializationContext : JsonSerializerContext;

public sealed class GameInitializer : ArchipelagoPacketHandler
{
    private readonly AsyncSubject<GameAndContext> _initializedGame = new();

    private readonly Settings _settings;

    private readonly IObserver<Exception> _unhandledException;

    private readonly Game _game;

    private FrozenDictionary<string, FrozenDictionary<long, string>>? _generalItemNameMapping;

    private FrozenDictionary<string, FrozenDictionary<long, string>>? _generalLocationNameMapping;

    private FrozenDictionary<int, SlotModel>? _slotInfo;

    private ImmutableArray<long> _locationIds;

    private FrozenDictionary<long, ItemKey>? _itemsById;

    private FrozenDictionary<long, LocationKey>? _locationsById;

    private FrozenDictionary<string, int>? _slotByPlayerAlias;

    private FrozenDictionary<int, string>? _playerAliasBySlot;

    private WeightedRandomItems<WeightedString>? _changedTargetMessages;

    private WeightedRandomItems<WeightedString>? _enteredGoModeMessages;

    private WeightedRandomItems<WeightedString>? _enterBKMessages;

    private WeightedRandomItems<WeightedString>? _remindBKMessages;

    private WeightedRandomItems<WeightedString>? _exitBKMessages;

    private WeightedRandomItems<WeightedString>? _completedGoalMessages;

    public GameInitializer(Settings settings, IObserver<Exception> unhandledException)
    {
        _settings = settings;
        _unhandledException = unhandledException;
        InitializedGame = _initializedGame.AsObservable();
        _game = new(Prng.State.Start());
    }

    public IObservable<GameAndContext> InitializedGame { get; }

    private int? _teamNumber;
    public int TeamNumber => _teamNumber ?? throw new InvalidOperationException("Team number has not been initialized yet.");

    private int? _slotNumber;
    public int SlotNumber => _slotNumber ?? throw new InvalidOperationException("Slot number has not been initialized yet.");

    private string ServerSavedStateKey => MultiworldInfo.GetServerSavedStateKey(TeamNumber, SlotNumber);

    public override async ValueTask HandleAsync(ArchipelagoPacketModel nextPacket, ArchipelagoPacketProvider sender, CancellationToken cancellationToken)
    {
        switch (nextPacket)
        {
            case RoomInfoPacketModel roomInfo:
                await HandleAsync(roomInfo, sender);
                break;

            case DataPackagePacketModel dataPackage:
                await HandleAsync(dataPackage, sender);
                break;

            case ConnectResponsePacketModel connectResponse:
                await HandleAsync(connectResponse, sender);
                break;

            case LocationInfoPacketModel locationInfo:
                Handle(locationInfo);
                break;

            case RoomUpdatePacketModel roomUpdate:
                Handle(roomUpdate);
                break;

            case ReceivedItemsPacketModel receivedItems:
                Handle(receivedItems);
                break;

            case RetrievedPacketModel retrieved:
                Handle(retrieved);
                break;

            case PrintJSONPacketModel printJSON:
                await HandleAsync(printJSON, sender);
                break;
        }
    }

    private static async ValueTask HandleAsync(RoomInfoPacketModel roomInfo, ArchipelagoPacketProvider sender)
    {
        GetDataPackagePacketModel getDataPackage = new() { Games = roomInfo.Games };
        await sender.SendPacketsAsync([getDataPackage]);
    }

    private async ValueTask HandleAsync(DataPackagePacketModel dataPackage, ArchipelagoPacketProvider sender)
    {
        GameDataModel autopelagoGameData = dataPackage.Data.Games["Autopelago"];
        long[] locationIds = new long[GameDefinitions.Instance.AllItems.Length];
        foreach ((string locationName, long locationId) in autopelagoGameData.LocationNameToId)
        {
            // value might be missing from the dictionary if there's a version mismatch. this code
            // runs BEFORE the explicit version mismatch check can possibly run, so we need to work
            // around those situations for *just* a little while longer...
            if (GameDefinitions.Instance.LocationsByName.TryGetValue(locationName, out LocationKey loc))
            {
                locationIds[loc.N] = locationId;
            }
        }

        _locationIds = ImmutableCollectionsMarshal.AsImmutableArray(locationIds);
        _itemsById = autopelagoGameData.ItemNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.ItemsByName.GetValueOrDefault(kvp.Key));
        _locationsById = autopelagoGameData.LocationNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.LocationsByName.GetValueOrDefault(kvp.Key));

        Dictionary<string, FrozenDictionary<long, string>> generalItemNameMapping = [];
        Dictionary<string, FrozenDictionary<long, string>> generalLocationNameMapping = [];
        foreach ((string gameName, GameDataModel gameData) in dataPackage.Data.Games)
        {
            // apparently, some unsupported games make it possible to have the name --> ID map show
            // multiple entries for the same ID, and they handle it just fine despite the two places
            // that I've found in the Archipelago documentation saying that these should be unique.
            // I guess it's possible that the same ID can go by multiple names? Regardless, if this
            // happens, then we can just pick one and move on.
            generalItemNameMapping.Add(gameName, gameData.ItemNameToId
                .GroupBy(kvp => kvp.Value, kvp => kvp.Key)
                .ToFrozenDictionary(grp => grp.Key, grp => grp.MinBy(s => s.Length)!));
            generalLocationNameMapping.Add(gameName, gameData.LocationNameToId
                .GroupBy(kvp => kvp.Value, kvp => kvp.Key)
                .ToFrozenDictionary(grp => grp.Key, grp => grp.MinBy(s => s.Length)!));
        }

        _generalItemNameMapping = generalItemNameMapping.ToFrozenDictionary();
        _generalLocationNameMapping = generalLocationNameMapping.ToFrozenDictionary();
        ConnectPacketModel connect = new()
        {
            Password = _settings.Password,
            Game = "Autopelago",
            Name = _settings.Slot,
            Uuid = Guid.NewGuid(),
            Version = new(new("0.6.1")),
            ItemsHandling = ArchipelagoItemsHandlingFlags.All,
            Tags = ["AP"],
            SlotData = true,
        };
        await sender.SendPacketsAsync([connect]);
    }

    private async ValueTask HandleAsync(ConnectResponsePacketModel connectResponse, ArchipelagoPacketProvider sender)
    {
        if (connectResponse is ConnectionRefusedPacketModel connectionRefused)
        {
            ConnectionRefusedException exception = new(connectionRefused.Errors);
            _initializedGame.OnError(exception);
            throw exception;
        }

        ConnectedPacketModel connected = (ConnectedPacketModel)connectResponse;

        if (!(TryDeserialize(connected.SlotData, out AutopelagoWorldMetadata? autopelagoWorldMetadata, out JsonException? thrownException) &&
              autopelagoWorldMetadata.VersionStamp == GameDefinitions.Instance.VersionStamp))
        {
            throw new InvalidOperationException($"""


Client and .apworld are from different versions! This client wants to see '{GameDefinitions.Instance.VersionStamp}'.
To check this yourself, open the .apworld file in 7-zip or something like that, look near the top of
its 'AutopelagoDefinitions.yml' file for a "version_stamp". If it's not there, or if its value isn't
the one we were looking for (again, '{GameDefinitions.Instance.VersionStamp}'), then that's why this happened.


""", thrownException);
        }

        _teamNumber = connected.Team;
        _slotNumber = connected.Slot;
        _changedTargetMessages = new(autopelagoWorldMetadata.ChangedTargetMessages);
        _enteredGoModeMessages = new(autopelagoWorldMetadata.EnteredGoModeMessages);
        _enterBKMessages = new(autopelagoWorldMetadata.EnterBKMessages);
        _remindBKMessages = new(autopelagoWorldMetadata.RemindBKMessages);
        _exitBKMessages = new(autopelagoWorldMetadata.ExitBKMessages);
        _completedGoalMessages = new(autopelagoWorldMetadata.CompletedGoalMessages);

        _game.InitializeLactoseIntolerance(autopelagoWorldMetadata.LactoseIntolerant);
        _game.InitializeVictoryLocation(GameDefinitions.Instance.LocationsByName[autopelagoWorldMetadata.VictoryLocationName]);
        GameDefinitions.Instance.TryGetLandmarkRegion(_game.VictoryLocation, out RegionKey victoryLandmark);
        BitArray384 locationIsReachable = GameDefinitions.Instance.GetLocationsBeforeVictoryLandmark(victoryLandmark);
        if (!autopelagoWorldMetadata.EnabledBuffs.IsDefault)
        {
            _game.InitializeEnabledBuffsAndTraps(autopelagoWorldMetadata.EnabledBuffs, autopelagoWorldMetadata.EnabledTraps);
        }

        _slotInfo = connected.SlotInfo.ToFrozenDictionary();
        _slotByPlayerAlias = connected.Players.ToFrozenDictionary(p => p.Alias, p => p.Slot);
        _playerAliasBySlot = connected.Players.ToFrozenDictionary(p => p.Slot, p => p.Alias);
        LocationScoutsPacketModel locationScouts = new()
        {
            Locations = _locationsById!
                .Where(kvp => locationIsReachable[kvp.Value.N])
                .Select(kvp => kvp.Key)
                .ToArray(),
        };

        GetPacketModel getPacket = new() { Keys = [ServerSavedStateKey] };
        await sender.SendPacketsAsync([locationScouts, getPacket]);
        LocationKey[] checkedLocations =
        [
            .. connected.CheckedLocations.Select(locationId => _locationsById![locationId]),
        ];
        _game.InitializeCheckedLocations(checkedLocations);
    }

    private static bool TryDeserialize(JsonElement json, [NotNullWhen(true)] out AutopelagoWorldMetadata? result, out JsonException? thrownException)
    {
        thrownException = null;
        try
        {
            result = json.Deserialize<AutopelagoWorldMetadata>(AutopelagoWorldMetadataSerializationContext.Default.AutopelagoWorldMetadata);
        }
        catch (JsonException ex)
        {
            result = null;
            thrownException = ex;
        }

        return result is not null;
    }

    private void Handle(LocationInfoPacketModel locationInfo)
    {
        Dictionary<ArchipelagoItemFlags, HashSet<LocationKey>> spoilerData = new()
        {
            // these two are always needed for Smart / Conspiratorial, regardless of what's in the
            // actual multiworld (e.g., maybe all traps are nonlocal).
            [ArchipelagoItemFlags.LogicalAdvancement] = [],
            [ArchipelagoItemFlags.Trap] = [],
        };

        foreach (ItemModel networkItem in locationInfo.Locations)
        {
            (CollectionsMarshal.GetValueRefOrAddDefault(spoilerData, networkItem.Flags, out _) ??= [])
                .Add(_locationsById![networkItem.Location]);
        }

        _game.InitializeSpoilerData(spoilerData.ToFrozenDictionary(kvp => kvp.Key, kvp => ToLocationsBitArray(kvp.Value)));
    }

    private static BitArray384 ToLocationsBitArray(HashSet<LocationKey> locations)
    {
        BitArray384 result = new(GameDefinitions.Instance.AllLocations.Length);
        foreach (LocationKey loc in locations)
        {
            result[loc.N] = true;
        }

        return result;
    }

    private void Handle(RoomUpdatePacketModel roomUpdate)
    {
        if (roomUpdate.SlotInfo is { } slotInfo)
        {
            _slotInfo = slotInfo.ToFrozenDictionary();
        }

        if (roomUpdate.Players is { } players)
        {
            _slotByPlayerAlias = players.ToFrozenDictionary(p => p.Alias, p => p.Slot);
            _playerAliasBySlot = players.ToFrozenDictionary(p => p.Slot, p => p.Alias);
        }
    }

    private void Handle(ReceivedItemsPacketModel receivedItems)
    {
        _game.InitializeReceivedItems(receivedItems.Items.Select(i => _itemsById![i.Item]));
    }

    private void Handle(RetrievedPacketModel retrieved)
    {
        if (!retrieved.Keys.TryGetValue(ServerSavedStateKey, out JsonElement serverSavedStateData))
        {
            return;
        }

        try
        {
            if (serverSavedStateData.Deserialize(ServerSavedStateSerializationContext.Default.ServerSavedState) is { } serverSavedState)
            {
                _game.InitializeServerSavedState(serverSavedState);
            }
        }
        catch (Exception ex)
        {
            // don't permanently stick the rat into an oddball state.
            Log.Fatal(ex, "Failed to deserialize previous state: {State}", JsonSerializer.SerializeToNode(serverSavedStateData, ServerSavedStateSerializationContext.Default.JsonElement)?.ToJsonString() ?? "(null)");
        }

        if (_generalItemNameMapping is not { } generalItemNameMapping ||
            _generalLocationNameMapping is not { } generalLocationNameMapping ||
            _slotInfo is not { } slotInfo ||
            _locationIds is not { IsDefault: false } locationIds ||
            _locationsById is not { } locationsById ||
            _itemsById is not { } itemsById ||
            _slotByPlayerAlias is not { } slotByPlayerAlias ||
            _playerAliasBySlot is not { } playerAliasBySlot ||
            _changedTargetMessages is not { } changedTargetMessages ||
            _enteredGoModeMessages is not { } enteredGoModeMessages ||
            _enterBKMessages is not { } enterBKMessages ||
            _remindBKMessages is not { } remindBKMessages ||
            _exitBKMessages is not { } exitBKMessages ||
            _completedGoalMessages is not { } completedGoalMessages)
        {
            HandshakeException exception = new();
            _unhandledException.OnError(exception);
            throw exception;
        }

        _game.EnsureStarted();
        _initializedGame.OnNext(new()
        {
            Game = _game,
            Context = new()
            {
                TeamNumber = TeamNumber,
                SlotNumber = SlotNumber,
                GeneralItemNameMapping = generalItemNameMapping,
                GeneralLocationNameMapping = generalLocationNameMapping,
                SlotInfo = slotInfo,
                LocationIds = locationIds,
                LocationsById = locationsById,
                ItemsById = itemsById,
                SlotByPlayerAlias = slotByPlayerAlias,
                PlayerAliasBySlot = playerAliasBySlot,
                ChangedTargetMessages = changedTargetMessages,
                EnteredGoModeMessages = enteredGoModeMessages,
                EnterBKMessages = enterBKMessages,
                RemindBKMessages = remindBKMessages,
                ExitBKMessages = exitBKMessages,
                CompletedGoalMessages = completedGoalMessages,
            },
        });
        _initializedGame.OnCompleted();
    }

    private async ValueTask HandleAsync(PrintJSONPacketModel printJSON, ArchipelagoPacketProvider sender)
    {
        if (_slotInfo is not { } slotInfo ||
            _generalItemNameMapping is not { } generalItemNameMapping ||
            _generalLocationNameMapping is not { } generalLocationNameMapping)
        {
            string simpleMessage = string.Concat(printJSON.Data.Select(p => p.Text));
            Log.Information(simpleMessage);
            await RespondToEarlyChatCommandIfNeededAsync(simpleMessage);
            return;
        }

        StringBuilder messageTemplateBuilder = new();
        Stack<IDisposable> ctxStack = [];
        try
        {
            int nextPlayerPlaceholder = 0;
            int nextItemPlaceholder = 0;
            int nextLocationPlaceholder = 0;
            foreach (JSONMessagePartModel part in printJSON.Data)
            {
                switch (part)
                {
                    case PlayerIdJSONMessagePartModel playerId:
                        string playerPlaceholder = $"Player{nextPlayerPlaceholder++}";
                        ctxStack.Push(LogContext.PushProperty(playerPlaceholder, slotInfo[int.Parse(playerId.Text)].Name));
                        messageTemplateBuilder.Append($"{{{playerPlaceholder}}}");
                        break;

                    case ItemIdJSONMessagePartModel itemId:
                        string gameForItem = slotInfo[itemId.Player].Game;
                        string itemPlaceholder = $"Item{nextItemPlaceholder++}";
                        ctxStack.Push(LogContext.PushProperty(itemPlaceholder, generalItemNameMapping[gameForItem][long.Parse(itemId.Text)]));
                        messageTemplateBuilder.Append($"{{{itemPlaceholder}}}");
                        break;

                    case LocationIdJSONMessagePartModel locationId:
                        string gameForLocation = slotInfo[locationId.Player].Game;
                        string locationPlaceholder = $"Location{nextLocationPlaceholder++}";
                        ctxStack.Push(LogContext.PushProperty(locationPlaceholder, generalLocationNameMapping[gameForLocation][long.Parse(locationId.Text)]));
                        messageTemplateBuilder.Append($"{{{locationPlaceholder}}}");
                        break;

                    default:
                        messageTemplateBuilder.Append(part.Text);
                        break;
                }
            }

            string message = $"{messageTemplateBuilder}";
            Log.Information(message);
            await RespondToEarlyChatCommandIfNeededAsync(message);
        }
        finally
        {
            while (ctxStack.TryPop(out IDisposable? ctx))
            {
                ctx.Dispose();
            }
        }

        async ValueTask RespondToEarlyChatCommandIfNeededAsync(string message)
        {
            if (message.Contains($"@{_settings.Slot}", StringComparison.InvariantCultureIgnoreCase))
            {
                await sender.SendPacketsAsync([new SayPacketModel
                {
                    Text = "Sorry, I'm still figuring stuff out, so I can't do anything with commands just yet. Try again later.",
                }]);
            }
        }
    }
}
