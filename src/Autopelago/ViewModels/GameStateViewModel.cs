using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Avalonia.ReactiveUI;

using DynamicData.Binding;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using Serilog;
using Serilog.Context;

namespace Autopelago.ViewModels;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ImmutableArray<ArchipelagoPacketModel>))]
internal sealed partial class SourceGenerationContext : JsonSerializerContext;

public sealed class GameStateViewModel : ViewModelBase, IDisposable
{
    private static readonly JsonReaderOptions s_jsonReaderOptions = new()
    {
        MaxDepth = 1000,
    };

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        TypeInfoResolver = SourceGenerationContext.Default,
    };

    private static readonly FrozenSet<string> s_hiddenProgressionItems = new[]
    {
        // these are the items marked as progression that aren't ever **individually** required.
        "pack_rat", "rat_pack",
    }.ToFrozenSet();

    private static readonly FrozenDictionary<string, int> s_progressionItemSortOrder = ProgressionItemSortOrder();

    private readonly CompositeDisposable _subscriptions = new();

    private readonly SemaphoreSlim _writerMutex = new(1);

    private readonly SemaphoreSlim _gameStateMutex = new(1);

    private readonly Task _gameLoopTask;

    private readonly Settings _settings;

    private readonly Player _player = new();

    private readonly FrozenDictionary<ItemDefinitionModel, CollectableItemViewModel> _collectableItemsByName;

    private ClientWebSocketBox _clientWebSocketBox = null!;

    private RoomInfoPacketModel _roomInfo = null!;

    private DataPackagePacketModel _dataPackage = null!;

    private ConnectedPacketModel _connected = null!;

    private RoomUpdatePacketModel? _lastRoomUpdate;

    private AutopelagoData _lastFullData = null!;

    private Game.State _prevState = Game.State.Start();

    public GameStateViewModel(Settings settings)
    {
        _settings = settings;

        ConnectionRefusedCommand = ReactiveCommand.Create<ConnectionRefusedPacketModel, ConnectionRefusedPacketModel>(x => x);
        _gameLoopTask = Task.Run(async () =>
        {
            try
            {
                await RunGameLoopAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Unhandled in game loop.");
                throw;
            }
        });

        _subscriptions.Add(Observable.Interval(TimeSpan.FromMilliseconds(500), AvaloniaScheduler.Instance)
            .Subscribe(_ =>
            {
                foreach (CheckableLocationViewModel loc in CheckableLocations)
                {
                    loc.NextFrame();
                }

                _gameStateMutex.Wait();
                try
                {
                    _prevState = _player.Advance(_prevState);
                }
                finally
                {
                    _gameStateMutex.Release();
                }
            }));

        FrozenDictionary<string, CollectableItemViewModel> progressionItemsLookup = ProgressionItems.ToFrozenDictionary(i => i.ItemKey);
        _collectableItemsByName = progressionItemsLookup.ToFrozenDictionary(kvp => kvp.Value.Model, kvp => kvp.Value);
        FrozenDictionary<string, CheckableLocationViewModel> checkableLocationsLookup = CheckableLocations.ToFrozenDictionary(l => l.LocationKey);
        FrozenDictionary<string, ImmutableArray<GameRequirementToolTipViewModel>> toolTipsByItem = (
            from loc in CheckableLocations
            from tt in loc.GameRequirementToolTipSource
            from tt2 in tt.DescendantsAndSelf()
            where tt2.Model is ReceivedItemRequirement
            group tt2 by ((ReceivedItemRequirement)tt2.Model).ItemKey
        ).ToFrozenDictionary(grp => grp.Key, grp => grp.ToImmutableArray());

        ImmutableArray<(int RatCount, GameRequirementToolTipViewModel ToolTip)> ratCountToolTips =
        [
            .. from loc in CheckableLocations
               from tt in loc.GameRequirementToolTipSource
               from tt2 in tt.DescendantsAndSelf()
               where tt2.Model is RatCountRequirement
               select (((RatCountRequirement)tt2.Model).RatCount, tt2),
        ];

        _subscriptions.Add(this
            .WhenAnyValue(x => x.RatCount)
            .Subscribe(ratCount =>
            {
                foreach ((int ratCountThreshold, GameRequirementToolTipViewModel toolTip) in ratCountToolTips)
                {
                    toolTip.Satisfied = ratCount >= ratCountThreshold;
                }
            }));

        foreach (CollectableItemViewModel item in ProgressionItems)
        {
            if (!toolTipsByItem.TryGetValue(item.ItemKey, out ImmutableArray<GameRequirementToolTipViewModel> tooltips))
            {
                continue;
            }

            _subscriptions.Add(item
                .WhenAnyValue(x => x.Collected)
                .Subscribe(collected =>
                {
                    foreach (GameRequirementToolTipViewModel tooltip in tooltips)
                    {
                        tooltip.Satisfied = collected;
                    }
                }));
        }

        _subscriptions.Add(ProgressionItemsCollected.ObserveCollectionChanges()
            .Select(c => c.EventArgs)
            .Where(args => args.Action == NotifyCollectionChangedAction.Add)
            .SelectMany(args => args.NewItems!.Cast<string>()
                .Where(progressionItemsLookup.ContainsKey)
                .Select(added => progressionItemsLookup[added]))
            .Subscribe(item => item.Collected = true));

        _subscriptions.Add(LocationsAvailable.ObserveCollectionChanges()
            .Select(c => c.EventArgs)
            .Where(args => args.Action == NotifyCollectionChangedAction.Add)
            .SelectMany(args => args.NewItems!.Cast<string>()
                .Where(checkableLocationsLookup.ContainsKey)
                .Select(added => checkableLocationsLookup[added]))
            .Subscribe(location => location.Available = true));

        _subscriptions.Add(LocationsChecked.ObserveCollectionChanges()
            .Select(c => c.EventArgs)
            .Where(args => args.Action == NotifyCollectionChangedAction.Add)
            .SelectMany(args => args.NewItems!.Cast<string>()
                .Where(checkableLocationsLookup.ContainsKey)
                .Select(added => checkableLocationsLookup[added]))
            .Subscribe(location => location.Checked = true));
    }

    [Reactive]
    public string SlotName { get; set; } = "";

    [Reactive]
    public int RatCount { get; set; }

    [Reactive]
    public int FoodFactor { get; set; }

    [Reactive]
    public int LuckFactor { get; set; }

    [Reactive]
    public int EnergyFactor { get; set; }

    [Reactive]
    public int StyleFactor { get; set; }

    [Reactive]
    public int DistractionCounter { get; set; }

    [Reactive]
    public bool HasConfidence { get; set; }

    public ReactiveCommand<ConnectionRefusedPacketModel, ConnectionRefusedPacketModel> ConnectionRefusedCommand { get; }

    public ImmutableArray<CollectableItemViewModel> ProgressionItems { get; } =
    [
        .. GameDefinitions.Instance.ProgressionItems.Keys
            .Where(itemKey => !s_hiddenProgressionItems.Contains(itemKey))
            .OrderBy(itemKey => s_progressionItemSortOrder[itemKey])
            .Select(key => new CollectableItemViewModel(key)),
    ];

    public ImmutableArray<CheckableLocationViewModel> CheckableLocations { get; } =
    [
        .. GameDefinitions.Instance.LandmarkRegions.Keys
            .Select(key => new CheckableLocationViewModel(key)),
    ];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ObservableCollectionExtended<string> ProgressionItemsCollected { get; } = [];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ObservableCollectionExtended<string> LocationsChecked { get; } = [];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ObservableCollectionExtended<string> LocationsAvailable { get; } = [];

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    private static FrozenDictionary<string, int> ProgressionItemSortOrder()
    {
        Dictionary<string, int> result = [];

        HashSet<RegionDefinitionModel> seenRegions = [];
        Queue<RegionDefinitionModel> regions = [];
        regions.Enqueue(GameDefinitions.Instance.StartRegion);
        while (regions.TryDequeue(out RegionDefinitionModel? region))
        {
            if (region is LandmarkRegionDefinitionModel)
            {
                region.Locations[0].Requirement.VisitItemKeys(itemKey => result.Add(itemKey, result.Count));
            }

            foreach (RegionExitDefinitionModel exit in region.Exits)
            {
                if (seenRegions.Add(exit.Region))
                {
                    regions.Enqueue(exit.Region);
                }
            }
        }

        return result.ToFrozenDictionary();
    }

    private static (ArchipelagoPacketModel Packet, long BytesConsumed)? TryGetNextPacket(ReadOnlySequence<byte> seq, bool endOfMessage, ref JsonReaderState readerState)
    {
        Utf8JsonReader reader = new(seq, endOfMessage, readerState);
        if (reader.TokenType == JsonTokenType.None && !reader.Read())
        {
            return null;
        }

        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.EndObject && !reader.Read())
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.EndArray)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Archipelago protocol error: each message must be a JSON array of JSON objects.");
        }

        if (!endOfMessage)
        {
            Utf8JsonReader testReader = reader;
            if (!testReader.TrySkip())
            {
                return null;
            }
        }

        ArchipelagoPacketModel packet = JsonSerializer.Deserialize<ArchipelagoPacketModel>(ref reader, s_jsonSerializerOptions)!;
        readerState = reader.CurrentState;
        return (packet, reader.BytesConsumed);
    }

    private async ValueTask SendPacketsAsync(ImmutableArray<ArchipelagoPacketModel> packets, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(packets, s_jsonSerializerOptions);
        await _writerMutex.WaitAsync(cancellationToken);
        try
        {
            await _clientWebSocketBox.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _writerMutex.Release();
        }
    }

    private async ValueTask HandleNextPacketAsync(ArchipelagoPacketModel packet)
    {
        switch (packet)
        {
            case RoomInfoPacketModel roomInfo:
                _roomInfo = roomInfo;
                GetDataPackagePacketModel getDataPackage = new() { Games = roomInfo.Games };
                await SendPacketsAsync([getDataPackage]);
                break;

            case DataPackagePacketModel dataPackage:
                _dataPackage = dataPackage;
                ConnectPacketModel connect = new()
                {
                    Password = _settings.Password,
                    Game = "Autopelago",
                    Name = _settings.Slot,
                    Uuid = Guid.NewGuid(),
                    Version = new(new("0.4.4")),
                    ItemsHandling = ArchipelagoItemsHandlingFlags.All,
                    Tags = ["AP"],
                    SlotData = true,
                };
                await SendPacketsAsync([connect]);
                break;

            case ConnectResponsePacketModel connectResponse:
                if (connectResponse is ConnectionRefusedPacketModel connectionRefused)
                {
                    await ConnectionRefusedCommand.Execute(connectionRefused);
                    break;
                }

                _connected = (ConnectedPacketModel)connectResponse;
                UpdateLastFullData();
                break;

            case RoomUpdatePacketModel roomUpdate:
                _lastRoomUpdate = roomUpdate;
                UpdateLastFullData();
                break;

            case ReceivedItemsPacketModel receivedItems:
                Handle(ref _prevState, receivedItems);
                break;

            case PrintJSONPacketModel printJSON:
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
                                ctxStack.Push(LogContext.PushProperty(playerPlaceholder, _lastFullData.SlotInfo[int.Parse(playerId.Text)].Name));
                                messageTemplateBuilder.Append($"{{{playerPlaceholder}}}");
                                break;

                            case ItemIdJSONMessagePartModel itemId:
                                string itemPlaceholder = $"Item{nextItemPlaceholder++}";
                                ctxStack.Push(LogContext.PushProperty(itemPlaceholder, _lastFullData.GeneralItemNameMapping[long.Parse(itemId.Text)]));
                                messageTemplateBuilder.Append($"{{{itemPlaceholder}}}");
                                break;

                            case LocationIdJSONMessagePartModel locationId:
                                string locationPlaceholder = $"Location{nextLocationPlaceholder++}";
                                ctxStack.Push(LogContext.PushProperty(locationPlaceholder, _lastFullData.GeneralLocationNameMapping[long.Parse(locationId.Text)]));
                                messageTemplateBuilder.Append($"{{{locationPlaceholder}}}");
                                break;

                            default:
                                messageTemplateBuilder.Append(part.Text);
                                break;
                        }
                    }

                    Log.Information($"{messageTemplateBuilder}");
                }
                finally
                {
                    while (ctxStack.TryPop(out IDisposable? ctx))
                    {
                        ctx.Dispose();
                    }
                }

                break;
        }
    }

    private async Task RunGameLoopAsync()
    {
        using ClientWebSocketBox socketBox = _clientWebSocketBox = new();
        try
        {
            await socketBox.Socket.ConnectAsync(new($"wss://{_settings.Host}:{_settings.Port}"), default);
        }
        catch (Exception ex)
        {
            try
            {
                // the socket actually disposes itself after ConnectAsync fails for practically
                // any reason (which is why we need to overwrite it with a new one here), but it
                // still makes me feel icky not to dispose it explicitly before overwriting it,
                // so just do it ourselves (airbreather 2024-01-12).
                await socketBox.Reset().ConnectAsync(new($"ws://{_settings.Host}:{_settings.Port}"), default);
            }
            catch (Exception ex2)
            {
                throw new AggregateException(ex, ex2);
            }
        }

        using IMemoryOwner<byte> firstBufOwner = MemoryPool<byte>.Shared.Rent(65536);
        Memory<byte> fullFirstBuf = firstBufOwner.Memory;
        Queue<IDisposable?> extraDisposables = [];
        while (true)
        {
            ValueWebSocketReceiveResult prevReceiveResult = await socketBox.Socket.ReceiveAsync(fullFirstBuf, default);
            ReadOnlyMemory<byte> firstBuf = fullFirstBuf[..prevReceiveResult.Count];
            if (firstBuf.IsEmpty)
            {
                continue;
            }

            // we're going to stream the objects in the array one-by-one.
            int startIndex = 0;
            JsonReaderState readerState = new(s_jsonReaderOptions);
            while (TryGetNextPacket(new(firstBuf[startIndex..]), prevReceiveResult.EndOfMessage, ref readerState) is (ArchipelagoPacketModel packet, long bytesConsumed))
            {
                startIndex = checked((int)(startIndex + bytesConsumed));
                await HandleNextPacketAsync(packet);
            }

            if (prevReceiveResult.EndOfMessage)
            {
                continue;
            }

            extraDisposables.Enqueue(null); // the first one lives through the entire outer loop.
            try
            {
                BasicSequenceSegment startSegment = new(firstBuf);
                BasicSequenceSegment endSegment = startSegment;
                while (!prevReceiveResult.EndOfMessage)
                {
                    IMemoryOwner<byte> nextBufOwner = MemoryPool<byte>.Shared.Rent(65536);
                    extraDisposables.Enqueue(nextBufOwner);
                    Memory<byte> fullNextBuf = nextBufOwner.Memory;
                    prevReceiveResult = await socketBox.Socket.ReceiveAsync(fullNextBuf, default);
                    endSegment = endSegment.Append(fullNextBuf[..prevReceiveResult.Count]);
                    while (TryGetNextPacket(new(startSegment, startIndex, endSegment, endSegment.Memory.Length), prevReceiveResult.EndOfMessage, ref readerState) is (ArchipelagoPacketModel packet, long bytesConsumed))
                    {
                        long totalBytesConsumed = startIndex + bytesConsumed;
                        while (totalBytesConsumed > startSegment.Memory.Length)
                        {
                            totalBytesConsumed -= startSegment.Memory.Length;
                            startSegment = (BasicSequenceSegment)startSegment.Next!;
                            extraDisposables.Dequeue()?.Dispose();
                        }

                        startIndex = checked((int)totalBytesConsumed);
                        await HandleNextPacketAsync(packet);
                    }
                }
            }
            finally
            {
                while (extraDisposables.TryDequeue(out IDisposable? disposable))
                {
                    disposable?.Dispose();
                }
            }
        }
    }

    private void UpdateLastFullData()
    {
        GameDataModel gameData = _dataPackage.Data.Games["Autopelago"];
        _lastFullData = new()
        {
            TeamNumber = _connected.Team,
            SlotNumber = _connected.Slot,
            GeneralItemNameMapping = _dataPackage.Data.Games.Values.SelectMany(game => game.ItemNameToId).ToFrozenDictionary(kvp => kvp.Value, kvp => kvp.Key),
            GeneralLocationNameMapping = _dataPackage.Data.Games.Values.SelectMany(game => game.LocationNameToId).ToFrozenDictionary(kvp => kvp.Value, kvp => kvp.Key),
            SlotInfo = (_lastRoomUpdate?.SlotInfo ?? _connected.SlotInfo).ToFrozenDictionary(),
            InitialSlotData = _connected.SlotData.ToFrozenDictionary(),
            ItemsMapping = gameData.ItemNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.ItemsByName[kvp.Key], kvp => kvp.Value),
            LocationsMapping = gameData.LocationNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.LocationsByName[kvp.Key], kvp => kvp.Value),
            ItemsReverseMapping = gameData.ItemNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.ItemsByName[kvp.Key]),
            LocationsReverseMapping = gameData.LocationNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.LocationsByName[kvp.Key]),
        };
    }

    private void Handle(ref Game.State state, ReceivedItemsPacketModel receivedItems)
    {
        var convertedItems = ImmutableArray.CreateRange(receivedItems.Items, (item, itemsReverseMapping) => itemsReverseMapping[item.Item], _lastFullData.ItemsReverseMapping);
        for (int i = receivedItems.Index; i < state.ReceivedItems.Count; i++)
        {
            if (convertedItems[i - receivedItems.Index] != state.ReceivedItems[i])
            {
                throw new NotImplementedException("Need to resync.");
            }
        }

        ImmutableArray<ItemDefinitionModel> newItems = convertedItems[(state.ReceivedItems.Count - receivedItems.Index)..];
        if (newItems.IsEmpty)
        {
            return;
        }

        int foodMod = 0;
        int energyFactorMod = 0;
        int luckFactorMod = 0;
        int distractedMod = 0;
        int stylishMod = 0;
        foreach (ItemDefinitionModel newItem in newItems)
        {
            if (_collectableItemsByName.TryGetValue(newItem, out CollectableItemViewModel? viewModel))
            {
                viewModel.Collected = true;
            }

            // "confidence" takes place right away: it could apply to another item in the batch.
            bool addConfidence = false;
            bool subtractConfidence = false;
            foreach (string aura in newItem.AurasGranted)
            {
                switch (aura)
                {
                    case "upset_tummy" when state.HasConfidence:
                    case "unlucky" when state.HasConfidence:
                    case "sluggish" when state.HasConfidence:
                    case "distracted" when state.HasConfidence:
                        subtractConfidence = true;
                        break;

                    case "well_fed":
                        ++foodMod;
                        break;

                    case "upset_tummy":
                        --foodMod;
                        break;

                    case "lucky":
                        ++luckFactorMod;
                        break;

                    case "unlucky":
                        --luckFactorMod;
                        break;

                    case "energized":
                        ++energyFactorMod;
                        break;

                    case "sluggish":
                        --energyFactorMod;
                        break;

                    case "distracted":
                        ++distractedMod;
                        break;

                    case "stylish":
                        ++stylishMod;
                        break;

                    case "confident":
                        addConfidence = true;
                        break;
                }
            }

            // subtract first
            if (subtractConfidence)
            {
                state = state with { HasConfidence = false };
            }

            if (addConfidence)
            {
                state = state with { HasConfidence = true };
            }
        }

        state = state with
        {
            ReceivedItems = state.ReceivedItems.AddRange(newItems),
            FoodFactor = state.FoodFactor + (foodMod * 5),
            EnergyFactor = state.EnergyFactor + (energyFactorMod * 5),
            LuckFactor = state.LuckFactor + luckFactorMod,
            StyleFactor = state.StyleFactor + (stylishMod * 2),
            DistractionCounter = state.DistractionCounter + distractedMod,
        };
    }

    private sealed record ClientWebSocketBox : IDisposable
    {
        public ClientWebSocketBox()
        {
            Socket = Reset();
        }

        public ClientWebSocket Socket { get; set; }

        public ClientWebSocket Reset()
        {
            using (Socket)
            {
                return Socket = new() { Options = { DangerousDeflateOptions = new() } };
            }
        }

        public void Dispose()
        {
            Socket.Dispose();
        }
    }

    private sealed record AutopelagoData
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
