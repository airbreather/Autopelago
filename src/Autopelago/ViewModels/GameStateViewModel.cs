using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

using Avalonia;
using Avalonia.Controls;
using Avalonia.ReactiveUI;

using DynamicData.Binding;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using Serilog;
using Serilog.Context;

namespace Autopelago.ViewModels;

public sealed partial class GameStateViewModel : ViewModelBase, IDisposable
{
    private const string AurasKey = "Auras";

    private static readonly JsonReaderOptions s_jsonReaderOptions = new()
    {
        MaxDepth = 1000,
    };

    private static readonly JsonTypeInfo<ArchipelagoPacketModel> s_packetTypeInfo =
        PacketSerializerContext.Default.ArchipelagoPacketModel;

    private static readonly JsonTypeInfo<ImmutableArray<ArchipelagoPacketModel>> s_packetsTypeInfo =
        PacketSerializerContext.Default.ImmutableArrayArchipelagoPacketModel;

    private static readonly FrozenSet<string> s_hiddenProgressionItems = new[]
    {
        // these are the items marked as progression that aren't ever **individually** required.
        "pack_rat", "rat_pack",

        // this one is fixed and doesn't really get sent by others.
        "blackbird",
    }.ToFrozenSet();

    private static readonly FrozenDictionary<string, int> s_progressionItemSortOrder = ProgressionItemSortOrder();

    private readonly TimeProvider _timeProvider = TimeProvider.System;

    private readonly CompositeDisposable _subscriptions = [];

    private readonly SemaphoreSlim _dataAvailableSignal = new(0);

    private readonly SemaphoreSlim _writerMutex = new(1);

    private readonly SemaphoreSlim _gameStateMutex = new(1);

    private readonly Settings _settings;

    private readonly FrozenDictionary<ItemDefinitionModel, CollectableItemViewModel> _collectableItemsByModel;

    private readonly FrozenDictionary<LocationDefinitionModel, LandmarkRegionViewModel> _landmarkRegionsByLocation;

    private readonly CancellationTokenSource _gameCompleteCts = new();

    private readonly Game _game = new(Prng.State.Start());

    private ClientWebSocketBox _clientWebSocketBox = null!;

    private DataPackagePacketModel _dataPackage = null!;

    private ConnectedPacketModel _connected = null!;

    private RoomUpdatePacketModel? _lastRoomUpdate;

    private AutopelagoData _lastFullData = null!;

    private Prng.State _intervalPrngState = Prng.State.Start();

    private TimeSpan _nextFullInterval;

    private long _prevStartTimestamp;

    private long? _prevBlockedReportTimestamp;

    private bool _wasGoMode;

    private CancellationTokenSource _pauseCts = new();

    private CancellationTokenSource _unpauseCts = new();

    public GameStateViewModel()
        : this(Settings.ForDesigner)
    {
    }

    public GameStateViewModel(Settings settings)
    {
        _settings = settings;
        _prevStartTimestamp = _timeProvider.GetTimestamp();

        PlayPauseCommand = ReactiveCommand.Create(() =>
        {
            if (Paused)
            {
                _unpauseCts.Cancel();
                Paused = false;
            }
            else
            {
                _pauseCts.Cancel();
                Paused = true;
            }
        });

        _subscriptions.Add(Observable.Interval(TimeSpan.FromMilliseconds(500), AvaloniaScheduler.Instance)
            .Where(_ => !Paused)
            .Subscribe(_ =>
            {
                foreach (LandmarkRegionViewModel landmark in LandmarkRegions)
                {
                    landmark.NextFrame();
                }
            }));

        FrozenDictionary<string, CollectableItemViewModel> progressionItemsLookup = ProgressionItems.ToFrozenDictionary(i => i.ItemKey);
        _collectableItemsByModel = progressionItemsLookup.ToFrozenDictionary(kvp => kvp.Value.Model, kvp => kvp.Value);
        FrozenDictionary<string, LandmarkRegionViewModel> landmarkRegionsLookup = LandmarkRegions.ToFrozenDictionary(l => l.RegionKey);
        _landmarkRegionsByLocation = landmarkRegionsLookup.ToFrozenDictionary(kvp => kvp.Value.Location, kvp => kvp.Value);
        FrozenDictionary<string, ImmutableArray<GameRequirementToolTipViewModel>> toolTipsByItem = (
            from loc in LandmarkRegions
            from tt in loc.GameRequirementToolTipSource.DescendantsAndSelf()
            where tt.Model is ReceivedItemRequirement
            group tt by ((ReceivedItemRequirement)tt.Model).ItemKey
        ).ToFrozenDictionary(grp => grp.Key, grp => grp.ToImmutableArray());

        ImmutableArray<(int RatCount, GameRequirementToolTipViewModel ToolTip)> ratCountToolTips = [
            .. from loc in LandmarkRegions
               from tt in loc.GameRequirementToolTipSource.DescendantsAndSelf()
               where tt.Model is RatCountRequirement
               select (((RatCountRequirement)tt.Model).RatCount, tt),
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

        _subscriptions.Add(LandmarksAvailable.ObserveCollectionChanges()
            .Select(c => c.EventArgs)
            .Where(args => args.Action == NotifyCollectionChangedAction.Add)
            .SelectMany(args => args.NewItems!.Cast<string>()
                .Where(landmarkRegionsLookup.ContainsKey)
                .Select(added => landmarkRegionsLookup[added]))
            .Subscribe(location => location.Available = true));

        _subscriptions.Add(LandmarksChecked.ObserveCollectionChanges()
            .Select(c => c.EventArgs)
            .Where(args => args.Action == NotifyCollectionChangedAction.Add)
            .SelectMany(args => args.NewItems!.Cast<string>()
                .Where(landmarkRegionsLookup.ContainsKey)
                .Select(added => landmarkRegionsLookup[added]))
            .Subscribe(location => location.Checked = true));

        FrozenDictionary<string, FillerRegionViewModel> fillerRegionLookup = GameDefinitions.Instance.FillerRegions
            .ToFrozenDictionary(kvp => kvp.Key, kvp => new FillerRegionViewModel(kvp.Value));
        FillerLocationPoints = [.. fillerRegionLookup.Values.SelectMany(r => r.LocationPoints).Select(p => p + FillerRegionViewModel.ToCenter)];

        _subscriptions.Add(this
            .WhenAnyValue(x => x.CurrentLocation)
            .Select(x => fillerRegionLookup.GetValueOrDefault(x.Key.RegionKey))
            .ToPropertyEx(this, x => x.CurrentFillerRegion));

        _subscriptions.Add(this
            .WhenAnyValue(x => x.CurrentLocation)
            .Select(x => landmarkRegionsLookup.GetValueOrDefault(x.Key.RegionKey))
            .ToPropertyEx(this, x => x.CurrentLandmarkRegion));

        _subscriptions.Add(this
            .WhenAnyValue(x => x.CurrentLocation)
            .Select(x => x.Key.N)
            .ToPropertyEx(this, x => x.CurrentRegionNum));

        _subscriptions.Add(this
            .WhenAnyValue(x => x.TargetLocation)
            .Select(x => fillerRegionLookup.GetValueOrDefault(x.Key.RegionKey))
            .ToPropertyEx(this, x => x.TargetFillerRegion));

        _subscriptions.Add(this
            .WhenAnyValue(x => x.TargetLocation)
            .Select(x => landmarkRegionsLookup.GetValueOrDefault(x.Key.RegionKey))
            .ToPropertyEx(this, x => x.TargetLandmarkRegion));

        _subscriptions.Add(this
            .WhenAnyValue(x => x.TargetLocation)
            .Select(x => x.Key.N)
            .ToPropertyEx(this, x => x.TargetRegionNum));

        _subscriptions.Add(this
            .WhenAnyValue(x => x.PreviousLocation)
            .Select(GetPoint)
            .ToPropertyEx(this, x => x.PreviousPoint));

        _subscriptions.Add(this
            .WhenAnyValue(x => x.CurrentLocation)
            .Select(GetPoint)
            .ToPropertyEx(this, x => x.CurrentPoint));

        _subscriptions.Add(this
            .WhenAnyValue(x => x.PreviousPoint, x => x.CurrentPoint)
            .Select(tup => GetTrueAngle(tup.Item1, tup.Item2))
            .ToPropertyEx(this, x => x.TrueAngle));

        _subscriptions.Add(this
            .WhenAnyValue(x => x.TrueAngle)
            .Select(angle => Math.Abs(angle) < 90 ? (double)1 : -1)
            .ToPropertyEx(this, x => x.ScaleX));

        _subscriptions.Add(this
            .WhenAnyValue(x => x.TrueAngle)
            .Select(angle => Math.Abs(angle) < 90 ? angle : angle - 180)
            .ToPropertyEx(this, x => x.RelativeAngle));

        if (Design.IsDesignMode)
        {
            IEnumerator<FillerRegionViewModel> fillerRegionEnumerator = Enumerable.Repeat(fillerRegionLookup.Values, 1_000_000)
                .SelectMany(x => x)
                .GetEnumerator();
            _subscriptions.Add(fillerRegionEnumerator);

            _subscriptions.Add(Observable
                .Interval(TimeSpan.FromSeconds(1), AvaloniaScheduler.Instance)
                .Where(_ => !Paused)
                .Subscribe(_ =>
                {
                    PreviousLocation = CurrentLocation;
                    CurrentLocation = NextLocation();
                    TargetLocation = CurrentLocation;
                }));

            LocationDefinitionModel NextLocation()
            {
                if (CurrentFillerRegion is not { } filler)
                {
                    filler = fillerRegionLookup["Menu"];
                }

                if (CurrentLocation.Key.N == filler.LocationPoints.Length - 1)
                {
                    fillerRegionEnumerator.MoveNext();
                    return fillerRegionEnumerator.Current!.Model.Locations[0];
                }

                return GameDefinitions.Instance.LocationsByKey[CurrentLocation.Key with { N = CurrentLocation.Key.N + 1 }];
            }
        }

        return;

        Point GetPoint(LocationDefinitionModel location)
        {
            return landmarkRegionsLookup.TryGetValue(location.Region.Key, out LandmarkRegionViewModel? landmark)
                ? landmark.CanvasLocation
                : fillerRegionLookup[location.Key.RegionKey].LocationPoints[location.Key.N];
        }

        double GetTrueAngle(Point prev, Point curr)
        {
            if (prev == curr)
            {
                return 0;
            }

            return Math.Atan2(curr.Y - prev.Y, curr.X - prev.X) * 180 / Math.PI;
        }
    }

    public static TimeSpan MovementAnimationTime { get; } = TimeSpan.FromSeconds(0.1);

    [Reactive]
    public string SlotName { get; set; } = "";

    public required ReactiveCommand<Unit, Unit> EndingFanfareCommand { get; init; }

    public required ReactiveCommand<Unit, Unit> BackToMainMenuCommand { get; init; }

    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }

    [Reactive]
    public bool Paused { get; private set; }

    [Reactive]
    public LocationDefinitionModel PreviousLocation { get; set; } = GameDefinitions.Instance.StartLocation;

    [Reactive]
    public LocationDefinitionModel CurrentLocation { get; set; } = GameDefinitions.Instance.StartLocation;

    [Reactive]
    public LocationDefinitionModel TargetLocation { get; set; } = GameDefinitions.Instance.StartLocation;

    [ObservableAsProperty]
    public FillerRegionViewModel? CurrentFillerRegion { get; }

    [ObservableAsProperty]
    public FillerRegionViewModel? TargetFillerRegion { get; }

    public ImmutableArray<Point> FillerLocationPoints { get; }

    [ObservableAsProperty]
    public int CurrentRegionNum { get; }

    [ObservableAsProperty]
    public int TargetRegionNum { get; }

    [ObservableAsProperty]
    public LandmarkRegionViewModel? CurrentLandmarkRegion { get; }

    [ObservableAsProperty]
    public LandmarkRegionViewModel? TargetLandmarkRegion { get; }

    [ObservableAsProperty]
    public Point PreviousPoint { get; }

    [ObservableAsProperty]
    public Point CurrentPoint { get; }

    [ObservableAsProperty]
    public double TrueAngle { get; }

    [ObservableAsProperty]
    public double RelativeAngle { get; }

    [ObservableAsProperty]
    public double ScaleX { get; }

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
    public int StartledCounter { get; set; }

    [Reactive]
    public bool HasConfidence { get; set; }

    private readonly Subject<Exception> _unhandledException = new();
    public IObservable<Exception> UnhandledException => _unhandledException.AsObservable();

    private readonly Subject<ConnectionRefusedPacketModel> _connectionRefused = new();
    public IObservable<ConnectionRefusedPacketModel> ConnectionRefused => _connectionRefused.AsObservable();

    public ImmutableArray<CollectableItemViewModel> ProgressionItems { get; } =
    [
        .. GameDefinitions.Instance.ProgressionItems.Keys
            .Where(itemKey => !s_hiddenProgressionItems.Contains(itemKey))
            .OrderBy(itemKey => s_progressionItemSortOrder[itemKey])
            .Select(key => new CollectableItemViewModel(key)),
    ];

    public ImmutableArray<LandmarkRegionViewModel> LandmarkRegions { get; } =
    [
        .. GameDefinitions.Instance.LandmarkRegions.Keys
            .Select(key => new LandmarkRegionViewModel(key)),
    ];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ObservableCollectionExtended<string> ProgressionItemsCollected { get; } = [];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ObservableCollectionExtended<string> LandmarksChecked { get; } = [];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ObservableCollectionExtended<string> LandmarksAvailable { get; } = [];

    public void Begin()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await RunPacketReadLoopAsync();
            }
            catch (Exception ex)
            {
                if (!_subscriptions.IsDisposed)
                {
                    Log.Fatal(ex, "Unhandled in packet read loop.");
                    _unhandledException.OnNext(ex);
                }
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await RunPlayLoopAsync();
            }
            catch (Exception ex)
            {
                if (!_subscriptions.IsDisposed)
                {
                    Log.Fatal(ex, "Unhandled in play loop.");
                    _unhandledException.OnNext(ex);
                }
            }
        });
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _clientWebSocketBox?.Dispose();
    }

    private static FrozenDictionary<string, int> ProgressionItemSortOrder()
    {
        Dictionary<string, int> result = [];

        HashSet<RegionDefinitionModel> seenRegions = [];
        Queue<RegionDefinitionModel> regions = [];
        regions.Enqueue(GameDefinitions.Instance.StartRegion);
        while (regions.TryDequeue(out RegionDefinitionModel? region))
        {
            if (region is LandmarkRegionDefinitionModel landmark)
            {
                landmark.Requirement.VisitItemKeys(itemKey => result.Add(itemKey, result.Count));
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

        ArchipelagoPacketModel packet = JsonSerializer.Deserialize(ref reader, s_packetTypeInfo)!;
        if (packet is PrintJSONPacketModel printJSON)
        {
            packet = printJSON.ToBestDerivedType();
        }

        readerState = reader.CurrentState;
        return (packet, reader.BytesConsumed);
    }

    private async ValueTask SendPacketsAsync(ImmutableArray<ArchipelagoPacketModel> packets, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(packets, s_packetsTypeInfo);
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
                    Version = new(new("0.5.0")),
                    ItemsHandling = ArchipelagoItemsHandlingFlags.All,
                    Tags = ["AP"],
                    SlotData = true,
                };
                await SendPacketsAsync([connect]);
                break;

            case ConnectResponsePacketModel connectResponse:
                if (connectResponse is ConnectionRefusedPacketModel connectionRefused)
                {
                    _connectionRefused.OnNext(connectionRefused);
                    break;
                }

                _connected = (ConnectedPacketModel)connectResponse;
                UpdateLastFullData();
                LocationScoutsPacketModel locationScouts = new()
                {
                    Locations = _lastFullData.LocationsById.Where(kvp => !kvp.Value.RewardIsFixed).Select(kvp => kvp.Key).ToArray(),
                };

                GetPacketModel getPacket = new() { Keys = [AurasKey] };
                ValueTask requestSpoilerTask = SendPacketsAsync([locationScouts, getPacket]);
                await _gameStateMutex.WaitAsync();
                try
                {
                    _game.InitializeCheckedLocations(
                        _connected.CheckedLocations
                            .Select(locationId => _lastFullData.LocationsById[locationId])
                    );

                    foreach (LocationDefinitionModel location in _game.CheckedLocations.InCheckedOrder)
                    {
                        if (_landmarkRegionsByLocation.TryGetValue(location, out var viewModel))
                        {
                            viewModel.Checked = true;
                        }
                    }
                }
                finally
                {
                    _gameStateMutex.Release();
                }

                await requestSpoilerTask;
                break;

            case LocationInfoPacketModel locationInfo:
                Dictionary<LocationDefinitionModel, ArchipelagoItemFlags> spoilerData = [];
                foreach (ItemModel networkItem in locationInfo.Locations)
                {
                    spoilerData[_lastFullData.LocationsById[networkItem.Location]] = networkItem.Flags;
                }

                _game.InitializeSpoilerData(spoilerData.ToFrozenDictionary());
                break;

            case RoomUpdatePacketModel roomUpdate:
                _lastRoomUpdate = roomUpdate;
                UpdateLastFullData();
                break;

            case ReceivedItemsPacketModel receivedItems:
                if (!_game.HasStarted)
                {
                    _game.InitializeReceivedItems(
                        receivedItems.Items
                            .Select(i => _lastFullData.ItemsById[i.Item])
                    );
                }
                else
                {
                    await _gameStateMutex.WaitAsync();
                    try
                    {
                        await SendPacketsAsync([
                            .. Handle(receivedItems),
                            new SetPacketModel
                            {
                                Key = AurasKey,
                                Operations =
                                [
                                    new()
                                    {
                                        Operation = ArchipelagoDataStorageOperationType.Replace,
                                        Value = JsonSerializer.SerializeToNode(
                                            _game.AuraData,
                                            AuraDataSerializationContext.Default.AuraData
                                        )!,
                                    },
                                ],
                            },
                        ]);

                        UpdateMeters();
                    }
                    finally
                    {
                        _gameStateMutex.Release();
                    }
                }

                break;

            case RetrievedPacketModel retrieved:
                if (!retrieved.Keys.TryGetValue(AurasKey, out JsonElement auras))
                {
                    break;
                }

                if (auras.Deserialize(AuraDataSerializationContext.Default.AuraData) is { } auraData)
                {
                    _game.InitializeAuraData(auraData);
                }

                _game.EnsureStarted();
                UpdateMeters();
                _dataAvailableSignal.Release();
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
                                string gameForItem = _lastFullData.SlotInfo[itemId.Player].Game;
                                string itemPlaceholder = $"Item{nextItemPlaceholder++}";
                                ctxStack.Push(LogContext.PushProperty(itemPlaceholder, _lastFullData.GeneralItemNameMapping[(gameForItem, long.Parse(itemId.Text))]));
                                messageTemplateBuilder.Append($"{{{itemPlaceholder}}}");
                                break;

                            case LocationIdJSONMessagePartModel locationId:
                                string gameForLocation = _lastFullData.SlotInfo[locationId.Player].Game;
                                string locationPlaceholder = $"Location{nextLocationPlaceholder++}";
                                ctxStack.Push(LogContext.PushProperty(locationPlaceholder, _lastFullData.GeneralLocationNameMapping[(gameForLocation, long.Parse(locationId.Text))]));
                                messageTemplateBuilder.Append($"{{{locationPlaceholder}}}");
                                break;

                            default:
                                messageTemplateBuilder.Append(part.Text);
                                break;
                        }
                    }

                    string message = $"{messageTemplateBuilder}";
                    Log.Information(message);
                    int tagIndex = message.IndexOf($"@{SlotName}", StringComparison.InvariantCultureIgnoreCase);
                    if (tagIndex >= 0)
                    {
                        await ProcessChatCommand(message, tagIndex);
                    }
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

    private async ValueTask ProcessChatCommand(string cmd, int tagIndex)
    {
        await Helper.ConfigureAwaitFalse();

        // chat message format is "{UserAlias}: {Message}", so it needs to be at least this long.
        if (tagIndex <= ": ".Length)
        {
            Log.Error("Unexpected message format, aborting: {Command}", cmd);
            return;
        }

        string probablyPlayerAlias = cmd[..(tagIndex - ": ".Length)];
        if (!_lastFullData.SlotByPlayerAlias.ContainsKey(probablyPlayerAlias))
        {
            // this isn't necessarily an error or a mistaken assumption. it could just be that the
            // "@{SlotName}" happened partway through their message. don't test every single user's
            // alias against every single chat message that contains "@{SlotName}", just require it
            // to be at the start of the message. done.
            return;
        }

        // if we got here, then the entire rest of the message after "@{SlotName}" is the command.
        cmd = Regex.Replace(cmd[tagIndex..], @$"^@{SlotName} ", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        if (cmd.StartsWith("go ", StringComparison.OrdinalIgnoreCase))
        {
            string loc = cmd["go ".Length..].Trim('"');
            if (GameDefinitions.Instance.LocationsByNameCaseInsensitive.TryGetValue(loc, out LocationDefinitionModel? toPrioritize))
            {
                await _gameStateMutex.WaitAsync();
                try
                {
                    _game.AddPriorityLocation(toPrioritize);
                }
                finally
                {
                    _gameStateMutex.Release();
                }

                SayPacketModel say = new()
                {
                    Text = $"All right, I'll get right over to '{toPrioritize.Name}', {probablyPlayerAlias}!",
                };
                await SendPacketsAsync([say]);
            }
            else
            {
                SayPacketModel say = new()
                {
                    Text = $"Um... excuse me, but... I don't know what a '{loc}' is...",
                };
                await SendPacketsAsync([say]);
            }
        }
        else if (cmd.StartsWith("stop ", StringComparison.OrdinalIgnoreCase))
        {
            string loc = cmd["stop ".Length..].Trim('"');
            LocationDefinitionModel? removed;
            await _gameStateMutex.WaitAsync();
            try
            {
                removed = _game.RemovePriorityLocation(loc);
            }
            finally
            {
                _gameStateMutex.Release();
            }

            SayPacketModel say = new()
            {
                Text = removed is null
                    ? $"Um... excuse me, but... I don't see a '{loc}' to remove..."
                    : $"Oh, OK. I'll stop trying to get to '{removed.Name}', {probablyPlayerAlias}.",
            };
            await SendPacketsAsync([say]);
        }
        else if (cmd.StartsWith("help", StringComparison.OrdinalIgnoreCase))
        {
            ImmutableArray<SayPacketModel> packets =
            [
                new() { Text = "Commands you can use are:" },
                new() { Text = $"1. @{SlotName} go LOCATION_NAME" },
                new() { Text = $"2. @{SlotName} stop LOCATION_NAME" },
                new() { Text = "LOCATION_NAME refers to whatever text you got in your hint, like \"Basketball\" or \"Before Prawn Stars #12\"." },
            ];
            await SendPacketsAsync(packets.CastArray<ArchipelagoPacketModel>());
        }
        else
        {
            SayPacketModel say = new()
            {
                Text = $"Say \"@{SlotName} help\" (without the quotes) for a list of commands.",
            };
            await SendPacketsAsync([say]);
        }
    }

    private async Task RunPacketReadLoopAsync()
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

        LandmarkRegionViewModel goalRegion = _landmarkRegionsByLocation[GameDefinitions.Instance.GoalLocation];
        using IMemoryOwner<byte> firstBufOwner = MemoryPool<byte>.Shared.Rent(65536);
        Memory<byte> fullFirstBuf = firstBufOwner.Memory;
        Queue<IDisposable?> extraDisposables = [];
        while (!goalRegion.Checked)
        {
            ValueWebSocketReceiveResult prevReceiveResult;
            try
            {
                prevReceiveResult = await socketBox.Socket.ReceiveAsync(fullFirstBuf, _gameCompleteCts.Token);
            }
            catch (OperationCanceledException)
            {
                continue;
            }

            ReadOnlyMemory<byte> firstBuf = fullFirstBuf[..prevReceiveResult.Count];
            if (firstBuf.IsEmpty)
            {
                continue;
            }

            // we're going to stream the objects in the array one-by-one.
            int startIndex = 0;
            JsonReaderState readerState = new(s_jsonReaderOptions);
            while (TryGetNextPacket(new(firstBuf[startIndex..]), prevReceiveResult.EndOfMessage, ref readerState) is ({ } packet, long bytesConsumed))
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
                    while (TryGetNextPacket(new(startSegment, startIndex, endSegment, endSegment.Memory.Length), prevReceiveResult.EndOfMessage, ref readerState) is ({ } packet, long bytesConsumed))
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

    private static readonly ImmutableArray<string> s_blockedMessages =
    [
        "I don't have anything to do right now. Go team!",
        "Hey, I'm completely stuck. But I still believe in you!",
        "I've run out of things to do. How are you?",
        "I'm out of things for now, gonna get a coffee. Anyone want something?",
    ];

    private static readonly ImmutableArray<string> s_unblockedMessages =
    [
        "Yippee, that's just what I needed!",
        "I'm back! I knew you could do it!",
        "Sweet, I'm unblocked! Thanks!",
        "Squeak-squeak, it's rattin' time!",
    ];

    private static readonly ImmutableArray<string> s_ratThoughts =
    [
        "the moon looks cheesy today",
        "i could really go for some cheddar",
        "squeak squeak",
        "wait, is there even air in space?",
        "i sure hope the moon hasn't gotten moldy",
        "wait... you can read my mind?",
        "did you know, real rats don't even like cheese all that much!",
        "don't you DARE call me a mouse!",
        "rat rat rat rat rat rat rat rat rat rat rat rat",
        "i may live in a sewer, but i'm squeaky clean!",
        "ahem, a little privacy please?",
        "you're not a cat, are you? just checking...",
    ];

    private async Task RunPlayLoopAsync()
    {
        _nextFullInterval = NextInterval();
        await _dataAvailableSignal.WaitAsync();
        while (!_game.IsCompleted)
        {
            TimeSpan remaining = _nextFullInterval - _timeProvider.GetElapsedTime(_prevStartTimestamp);
            if (remaining > TimeSpan.Zero)
            {
                long dueTime = _timeProvider.GetTimestamp() + ((long)(_nextFullInterval.TotalSeconds * _timeProvider.TimestampFrequency));
                while (_timeProvider.GetTimestamp() < dueTime)
                {
                    try
                    {
                        await Task.Delay(remaining, _timeProvider, _pauseCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (_subscriptions.IsDisposed)
                        {
                            return;
                        }

                        // user clicked Pause
                        long waitStart = _timeProvider.GetTimestamp();
                        TaskCompletionSource cancelTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        await using CancellationTokenRegistration reg = _unpauseCts.Token.Register(() => cancelTcs.TrySetResult());
                        await cancelTcs.Task;
                        dueTime += _timeProvider.GetTimestamp() - waitStart;
                        _unpauseCts = new();
                        _pauseCts = new();
                    }

                    if (_subscriptions.IsDisposed)
                    {
                        return;
                    }
                }
            }

            async ValueTask CheckGoMode()
            {
                if (!_wasGoMode && _game.TargetLocationReason == TargetLocationReason.GoMode)
                {
                    SayPacketModel say = new()
                    {
                        Text = "That's it! I have everything I need! The moon is in sight!",
                    };
                    await SendPacketsAsync([say]);
                    _wasGoMode = true;
                }
            }

            int prevCheckedLocationsCount;
            _prevStartTimestamp = _timeProvider.GetTimestamp();
            await _gameStateMutex.WaitAsync();
            try
            {
                prevCheckedLocationsCount = _game.CheckedLocations.Count;
                _nextFullInterval = NextInterval();
                await CheckGoMode();
                _game.Advance();
                await CheckGoMode();
            }
            finally
            {
                _gameStateMutex.Release();
            }

            List<long> locationIds = [];
            foreach (LocationDefinitionModel location in _game.CheckedLocations.InCheckedOrder.Skip(prevCheckedLocationsCount))
            {
                locationIds.Add(_lastFullData.LocationIds[location]);
                if (_landmarkRegionsByLocation.TryGetValue(location, out LandmarkRegionViewModel? viewModel))
                {
                    viewModel.Checked = true;
                }
            }

            if (locationIds.Count > 0)
            {
                LocationChecksPacketModel locationChecks = new() { Locations = locationIds.ToArray() };
                await SendPacketsAsync([locationChecks]);
            }

            TargetLocation = _game.TargetLocation;
            for (int i = 0; i < _game.PreviousStepMovementLog.Length; i++)
            {
                LocationVector mov = _game.PreviousStepMovementLog[i];
                PreviousLocation = mov.PreviousLocation;
                CurrentLocation = mov.CurrentLocation;
                if (i == _game.PreviousStepMovementLog.Length - 1)
                {
                    // don't delay the last movement, Avalonia's easing will take care of it.
                    continue;
                }

                await Task.Delay(MovementAnimationTime, _timeProvider);
                if (!Paused)
                {
                    continue;
                }

                TaskCompletionSource cancelTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await using CancellationTokenRegistration reg = _unpauseCts.Token.Register(() => cancelTcs.TrySetResult());
                await cancelTcs.Task;
                _unpauseCts = new();
                _pauseCts = new();
            }

            UpdateMeters();

            if (_game.TargetLocationReason == TargetLocationReason.NowhereUsefulToMove)
            {
                if (_prevBlockedReportTimestamp is long prevBlockedReportTimestamp)
                {
                    if (Stopwatch.GetElapsedTime(prevBlockedReportTimestamp).TotalMinutes >= 15)
                    {
                        _prevBlockedReportTimestamp = null;
                    }
                }

                if (_prevBlockedReportTimestamp is null)
                {
                    await SendPacketsAsync([new SayPacketModel
                    {
                        Text = s_blockedMessages[Random.Shared.Next(s_blockedMessages.Length)],
                    }]);
                    _prevBlockedReportTimestamp = Stopwatch.GetTimestamp();
                }
            }
            else
            {
                if (_prevBlockedReportTimestamp is not null)
                {
                    await SendPacketsAsync([new SayPacketModel
                    {
                        Text = s_unblockedMessages[Random.Shared.Next((s_unblockedMessages.Length))],
                    }]);
                    _prevBlockedReportTimestamp = null;
                }
            }
        }

        _landmarkRegionsByLocation[GameDefinitions.Instance.GoalLocation].Checked = true;
        StatusUpdatePacketModel statusUpdate = new() { Status = ArchipelagoClientStatus.Goal };
        await SendPacketsAsync([statusUpdate]);
        await EndingFanfareCommand.Execute();
        await _gameCompleteCts.CancelAsync();
    }

    private void UpdateLastFullData()
    {
        GameDataModel gameData = _dataPackage.Data.Games["Autopelago"];
        _lastFullData = new()
        {
            GeneralItemNameMapping = _dataPackage.Data.Games.SelectMany(game => game.Value.ItemNameToId.Select(kvp => (Game: game.Key, ItemName: kvp.Key, ItemId: kvp.Value))).ToFrozenDictionary(tup => (tup.Game, tup.ItemId), tup => tup.ItemName),
            GeneralLocationNameMapping = _dataPackage.Data.Games.SelectMany(game => game.Value.LocationNameToId.Select(kvp => (Game: game.Key, LocationName: kvp.Key, LocationId: kvp.Value))).ToFrozenDictionary(tup => (tup.Game, tup.LocationId), tup => tup.LocationName),
            SlotInfo = (_lastRoomUpdate?.SlotInfo ?? _connected.SlotInfo).ToFrozenDictionary(),
            LocationIds = gameData.LocationNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.LocationsByName[kvp.Key], kvp => kvp.Value),
            ItemsById = gameData.ItemNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.ItemsByName[kvp.Key]),
            LocationsById = gameData.LocationNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.LocationsByName[kvp.Key]),
            SlotByPlayerAlias = (_lastRoomUpdate?.Players ?? _connected.Players).ToFrozenDictionary(p => p.Alias, p => p.Slot),
        };
    }

    private static readonly ImmutableArray<string> s_newTargetPhrases =
    [
        "Oh, hey, what's that thing over there at '{LOCATION}'?",
        "There's something at '{LOCATION}', I'm sure of it!",
        "Something at '{LOCATION}' smells good!",
        "There's a rumor that something's going on at '{LOCATION}'!",
    ];

    private ImmutableArray<SayPacketModel> Handle(ReceivedItemsPacketModel receivedItems)
    {
        var convertedItems = ImmutableArray.CreateRange(receivedItems.Items, (item, itemsReverseMapping) => itemsReverseMapping[item.Item], _lastFullData.ItemsById);
        for (int i = receivedItems.Index; i < _game.ReceivedItems.Count; i++)
        {
            if (convertedItems[i - receivedItems.Index] != _game.ReceivedItems.InReceivedOrder[i])
            {
                throw new InvalidOperationException("Need to resync. Try connecting again.");
            }
        }

        ImmutableArray<ItemDefinitionModel> newItems = convertedItems[(_game.ReceivedItems.Count - receivedItems.Index)..];
        int priorityPriorityLocationCountBefore = _game.PriorityPriorityLocations.Count;
        _game.ReceiveItems(newItems);

        if (priorityPriorityLocationCountBefore == _game.PriorityPriorityLocations.Count)
        {
            return [];
        }

        List<SayPacketModel> newPackets = [];
        foreach (LocationDefinitionModel newPriorityLocation in _game.PriorityPriorityLocations.Skip(priorityPriorityLocationCountBefore))
        {
            newPackets.Add(new()
            {
                Text = s_newTargetPhrases[Random.Shared.Next(s_newTargetPhrases.Length)].Replace("{LOCATION}", newPriorityLocation.Name),
            });
        }

        return [.. newPackets];
    }

    private TimeSpan NextInterval()
    {
        double rangeSeconds = (double)(_settings.MaxStepSeconds - _settings.MinStepSeconds);
        double baseInterval = (double)_settings.MinStepSeconds + (rangeSeconds * Prng.NextDouble(ref _intervalPrngState));
        return TimeSpan.FromSeconds(baseInterval);
    }

    private void UpdateMeters()
    {
        foreach (ItemDefinitionModel item in _game.ReceivedItems.InReceivedOrder)
        {
            if (_collectableItemsByModel.TryGetValue(item, out CollectableItemViewModel? viewModel))
            {
                viewModel.Collected = true;
            }
        }

        RatCount = _game.ReceivedItems.RatCount;
        FoodFactor = _game.FoodFactor;
        EnergyFactor = _game.EnergyFactor;
        LuckFactor = _game.LuckFactor;
        StyleFactor = _game.StyleFactor;
        DistractionCounter = _game.DistractionCounter;
        StartledCounter = _game.StartledCounter;
        HasConfidence = _game.HasConfidence;
    }

    [Reactive]
    public string RatThought { get; set; } = s_ratThoughts[0];

    public void NextRatThought()
    {
        string prevRatThought = RatThought;
        string nextRatThought = RatThought;
        while (nextRatThought == prevRatThought)
        {
            nextRatThought = s_ratThoughts[Random.Shared.Next(s_ratThoughts.Length)];
        }

        RatThought = nextRatThought;
    }

    private sealed record ClientWebSocketBox : IDisposable
    {
        public ClientWebSocketBox()
        {
            Socket = Reset();
        }

        public ClientWebSocket Socket { get; private set; }

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
        public required FrozenDictionary<(string GameName, long ItemId), string> GeneralItemNameMapping { get; init; }

        public required FrozenDictionary<(string GameName, long LocationId), string> GeneralLocationNameMapping { get; init; }

        public required FrozenDictionary<int, SlotModel> SlotInfo { get; init; }

        public required FrozenDictionary<LocationDefinitionModel, long> LocationIds { get; init; }

        public required FrozenDictionary<long, ItemDefinitionModel> ItemsById { get; init; }

        public required FrozenDictionary<long, LocationDefinitionModel> LocationsById { get; init; }

        public required FrozenDictionary<string, int> SlotByPlayerAlias { get; init; }
    }

    [JsonSerializable(typeof(AuraData))]
    private sealed partial class AuraDataSerializationContext : JsonSerializerContext;
}
