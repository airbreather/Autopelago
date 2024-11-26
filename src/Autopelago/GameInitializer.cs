using System.Collections.Frozen;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Serilog;
using Serilog.Context;

namespace Autopelago;

using static Constants;

public sealed class GameAndContext
{
    public required Game Game { get; init; }

    public required MultiworldInfo Context { get; init; }
}

public sealed record MultiworldInfo
{
    public required FrozenDictionary<string, FrozenDictionary<long, string>> GeneralItemNameMapping { get; init; }

    public required FrozenDictionary<string, FrozenDictionary<long, string>> GeneralLocationNameMapping { get; init; }

    public required FrozenDictionary<int, SlotModel> SlotInfo { get; init; }

    public required FrozenDictionary<LocationDefinitionModel, long> LocationIds { get; init; }

    public required FrozenDictionary<long, LocationDefinitionModel> LocationsById { get; init; }

    public required FrozenDictionary<long, ItemDefinitionModel> ItemsById { get; init; }

    public required FrozenDictionary<string, int> SlotByPlayerAlias { get; init; }
}

[JsonSerializable(typeof(AuraData))]
public sealed partial class AuraDataSerializationContext : JsonSerializerContext;

public sealed class GameInitializer : ArchipelagoPacketHandler
{
    private readonly AsyncSubject<GameAndContext> _initializedGame = new();

    private readonly Settings _settings;

    private readonly IObserver<Exception> _unhandledException;

    private readonly Game _game;

    private FrozenDictionary<string, FrozenDictionary<long, string>>? _generalItemNameMapping;

    private FrozenDictionary<string, FrozenDictionary<long, string>>? _generalLocationNameMapping;

    private FrozenDictionary<int, SlotModel>? _slotInfo;

    private FrozenDictionary<LocationDefinitionModel, long>? _locationIds;

    private FrozenDictionary<long, ItemDefinitionModel>? _itemsById;

    private FrozenDictionary<long, LocationDefinitionModel>? _locationsById;

    private FrozenDictionary<string, int>? _slotByPlayerAlias;

    public GameInitializer(Settings settings, IObserver<Exception> unhandledException)
    {
        _settings = settings;
        _unhandledException = unhandledException;
        InitializedGame = _initializedGame.AsObservable();
        _game = new(Prng.State.Start());
    }

    public IObservable<GameAndContext> InitializedGame { get; }

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
        _locationIds = autopelagoGameData.LocationNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.LocationsByName[kvp.Key], kvp => kvp.Value);
        _itemsById = autopelagoGameData.ItemNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.ItemsByName[kvp.Key]);
        _locationsById = autopelagoGameData.LocationNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.LocationsByName[kvp.Key]);

        Dictionary<string, FrozenDictionary<long, string>> generalItemNameMapping = [];
        Dictionary<string, FrozenDictionary<long, string>> generalLocationNameMapping = [];
        foreach ((string gameName, GameDataModel gameData) in dataPackage.Data.Games)
        {
            generalItemNameMapping.Add(gameName, gameData.ItemNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => kvp.Key));
            generalLocationNameMapping.Add(gameName, gameData.LocationNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => kvp.Key));
        }

        _generalItemNameMapping = generalItemNameMapping.ToFrozenDictionary();
        _generalLocationNameMapping = generalLocationNameMapping.ToFrozenDictionary();
        ConnectPacketModel connect = new()
        {
            Password = _settings.Password,
            Game = "Autopelago",
            Name = _settings.Slot,
            Uuid = Guid.NewGuid(),
            Version = new(new("0.5.0")),
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
        _slotInfo = connected.SlotInfo.ToFrozenDictionary();
        _slotByPlayerAlias = connected.Players.ToFrozenDictionary(p => p.Alias, p => p.Slot);
        LocationScoutsPacketModel locationScouts = new()
        {
            Locations = _locationsById!.Where(kvp => !kvp.Value.RewardIsFixed).Select(kvp => kvp.Key).ToArray(),
        };

        GetPacketModel getPacket = new() { Keys = [AurasKey] };
        await sender.SendPacketsAsync([locationScouts, getPacket]);
        LocationDefinitionModel[] checkedLocations =
        [
            .. connected.CheckedLocations.Select(locationId => _locationsById![locationId]),
        ];
        _game.InitializeCheckedLocations(checkedLocations);
    }

    private void Handle(LocationInfoPacketModel locationInfo)
    {
        Dictionary<LocationDefinitionModel, ArchipelagoItemFlags> spoilerData = [];
        foreach (ItemModel networkItem in locationInfo.Locations)
        {
            spoilerData[_locationsById![networkItem.Location]] = networkItem.Flags;
        }

        _game.InitializeSpoilerData(spoilerData.ToFrozenDictionary());
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
        }
    }

    private void Handle(ReceivedItemsPacketModel receivedItems)
    {
        _game.InitializeReceivedItems(receivedItems.Items.Select(i => _itemsById![i.Item]));
    }

    private void Handle(RetrievedPacketModel retrieved)
    {
        if (!retrieved.Keys.TryGetValue(AurasKey, out JsonElement auras))
        {
            return;
        }

        try
        {
            if (auras.Deserialize(AuraDataSerializationContext.Default.AuraData) is { } auraData)
            {
                _game.InitializeAuraData(auraData);
            }
        }
        catch (Exception ex)
        {
            // don't permanently stick the rat into an oddball state.
            Log.Fatal(ex, "Failed to deserialize auras: {Auras}", JsonSerializer.SerializeToNode(auras)?.ToJsonString() ?? "(null)");
        }

        if (_generalItemNameMapping is not { } generalItemNameMapping ||
            _generalLocationNameMapping is not { } generalLocationNameMapping ||
            _slotInfo is not { } slotInfo ||
            _locationIds is not { } locationIds ||
            _locationsById is not { } locationsById ||
            _itemsById is not { } itemsById ||
            _slotByPlayerAlias is not { } slotByPlayerAlias)
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
                GeneralItemNameMapping = generalItemNameMapping,
                GeneralLocationNameMapping = generalLocationNameMapping,
                SlotInfo = slotInfo,
                LocationIds = locationIds,
                LocationsById = locationsById,
                ItemsById = itemsById,
                SlotByPlayerAlias = slotByPlayerAlias,
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
                SayPacketModel say = new()
                {
                    Text = "Sorry, I'm still figuring stuff out, so I can't do anything with commands just yet. Try again later.",
                };
                await sender.SendPacketsAsync([say]);
            }
        }
    }
}
