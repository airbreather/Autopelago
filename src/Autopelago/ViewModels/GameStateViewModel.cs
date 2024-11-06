using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net.WebSockets;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
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

using ZstdSharp;

namespace Autopelago.ViewModels;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(GameState.GameStateProxy))]
internal sealed partial class GameStateProxySerializerContext : JsonSerializerContext;

public sealed class GameStateViewModel : ViewModelBase, IDisposable
{
    private static readonly JsonReaderOptions s_jsonReaderOptions = new()
    {
        MaxDepth = 1000,
    };

    private static readonly FileStreamOptions s_readOptions = new()
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.ReadWrite | FileShare.Delete,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
    };

    private static readonly FileStreamOptions s_writeOptions = new()
    {
        Mode = FileMode.Create,
        Access = FileAccess.ReadWrite,
        Share = FileShare.Read | FileShare.Delete,
        Options = FileOptions.Asynchronous,
    };

    private static readonly JsonTypeInfo<ArchipelagoPacketModel> s_packetTypeInfo =
        PacketSerializerContext.Default.ArchipelagoPacketModel;

    private static readonly JsonTypeInfo<ImmutableArray<ArchipelagoPacketModel>> s_packetsTypeInfo =
        PacketSerializerContext.Default.ImmutableArrayArchipelagoPacketModel;

    private static readonly JsonTypeInfo<GameState.GameStateProxy> s_gameStateProxyTypeInfo =
        GameStateProxySerializerContext.Default.GameStateProxy;

    private static readonly FrozenSet<string> s_hiddenProgressionItems = new[]
    {
        // these are the items marked as progression that aren't ever **individually** required.
        "pack_rat", "rat_pack",

        // this one is fixed and doesn't really get sent by others.
        "blackbird",
    }.ToFrozenSet();

    private static readonly FrozenDictionary<string, int> s_progressionItemSortOrder = ProgressionItemSortOrder();

    private readonly FileInfo _stateFile;

    private readonly FileInfo _stateTmpFile;

    private readonly TimeProvider _timeProvider = TimeProvider.System;

    private readonly CompositeDisposable _subscriptions = new();

    private readonly SemaphoreSlim _dataAvailableSignal = new(0);

    private readonly SemaphoreSlim _writerMutex = new(1);

    private readonly SemaphoreSlim _gameStateMutex = new(1);

    private readonly Settings _settings;

    private readonly Player _player = new();

    private readonly FrozenDictionary<ItemDefinitionModel, CollectableItemViewModel> _collectableItemsByModel;

    private readonly FrozenDictionary<LocationDefinitionModel, LandmarkRegionViewModel> _landmarkRegionsByLocation;

    private readonly CancellationTokenSource _gameCompleteCts = new();

    private ClientWebSocketBox _clientWebSocketBox = null!;

    private DataPackagePacketModel _dataPackage = null!;

    private ConnectedPacketModel _connected = null!;

    private RoomUpdatePacketModel? _lastRoomUpdate;

    private AutopelagoData _lastFullData = null!;

    private FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags> _spoilerData = null!;

    private GameState _state = GameState.Start();

    private Prng.State _intervalPrngState = Prng.State.Start();

    private TimeSpan _nextFullInterval;

    private long _prevStartTimestamp;

    private CancellationTokenSource _pauseCts = new();

    private CancellationTokenSource _playCts = new();

    public GameStateViewModel()
        : this(Settings.ForDesigner)
    {
    }

    public GameStateViewModel(Settings settings)
    {
        _settings = settings;
        _prevStartTimestamp = _timeProvider.GetTimestamp();
        _stateFile = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autopelago", $"{settings.Host}_{settings.Port}_{settings.Slot}.json.zst"));
        _stateFile.Directory!.Create();
        _stateTmpFile = new(_stateFile.FullName.Replace(".json.zst", ".tmp.json.zst"));

        PlayPauseCommand = ReactiveCommand.Create(() =>
        {
            if (Paused)
            {
                _playCts.Cancel();
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
            .WhenAnyValue(x => x.CurrentLandmarkRegion, x => x.CurrentFillerRegion, x => x.CurrentRegionNum)
            .Select(tup => tup.Item1?.CanvasLocation ?? (tup.Item2 ?? fillerRegionLookup["Menu"]).LocationPoints.ElementAtOrDefault(tup.Item3))
            .ToPropertyEx(this, x => x.CurrentPoint));

        if (Design.IsDesignMode)
        {
            IEnumerator<FillerRegionViewModel> fillerRegionEnumerator = Enumerable.Repeat(fillerRegionLookup.Values, 1_000_000)
                .SelectMany(x => x)
                .GetEnumerator();
            _subscriptions.Add(fillerRegionEnumerator);

            _subscriptions.Add(Observable
                .Interval(TimeSpan.FromSeconds(1), AvaloniaScheduler.Instance)
                .Where(_ => !Paused)
                .Subscribe(_ => CurrentLocation = NextLocation()));

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
    }

    [Reactive]
    public string SlotName { get; set; } = "";

    public required ReactiveCommand<Unit, Unit> EndingFanfareCommand { get; init; }

    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }

    [Reactive]
    public bool Paused { get; private set; }

    [Reactive]
    public LocationDefinitionModel CurrentLocation { get; set; } = GameDefinitions.Instance.StartLocation;

    [ObservableAsProperty]
    public FillerRegionViewModel? CurrentFillerRegion { get; }

    [ObservableAsProperty]
    public int CurrentRegionNum { get; }

    [ObservableAsProperty]
    public LandmarkRegionViewModel? CurrentLandmarkRegion { get; }

    [ObservableAsProperty]
    public Point CurrentPoint { get; }

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
                Log.Fatal(ex, "Unhandled in packet read loop.");
                _unhandledException.OnNext(ex);
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
                Log.Fatal(ex, "Unhandled in play loop.");
                _unhandledException.OnNext(ex);
            }
        });
    }

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
                ValueTask requestSpoilerTask = SendPacketsAsync([locationScouts]);
                await _gameStateMutex.WaitAsync();
                try
                {
                    HashSet<LocationDefinitionModel> knownCheckedLocations = [.. _state.CheckedLocations];
                    HashSet<LocationDefinitionModel> newCheckedLocations = [];
                    foreach (long locationId in _connected.CheckedLocations)
                    {
                        LocationDefinitionModel location = _lastFullData.LocationsById[locationId];
                        if (!knownCheckedLocations.Remove(location))
                        {
                            newCheckedLocations.Add(location);
                        }
                    }

                    if (knownCheckedLocations.Count + newCheckedLocations.Count != 0)
                    {
                        _state = _state with
                        {
                            CheckedLocations = [
                                .. _state.CheckedLocations
                                    .Except(knownCheckedLocations)
                                    .Concat(newCheckedLocations),
                            ],
                        };
                    }

                    foreach (LocationDefinitionModel location in _state.CheckedLocations)
                    {
                        if (_landmarkRegionsByLocation.TryGetValue(location, out var viewModel))
                        {
                            viewModel.Checked = true;
                        }
                    }

                    await SaveAsync();
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

                _spoilerData = spoilerData.ToFrozenDictionary();
                _dataAvailableSignal.Release();
                break;

            case RoomUpdatePacketModel roomUpdate:
                _lastRoomUpdate = roomUpdate;
                UpdateLastFullData();
                break;

            case ReceivedItemsPacketModel receivedItems:
                await _gameStateMutex.WaitAsync();
                try
                {
                    ulong prevEpoch = _state.Epoch;
                    Handle(ref _state, receivedItems);
                    if (_state.Epoch != prevEpoch)
                    {
                        await SaveAsync();
                    }
                }
                finally
                {
                    _gameStateMutex.Release();
                }

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
        if (!_lastFullData.SlotByPlayerAlias.TryGetValue(probablyPlayerAlias, out int chattingSlot))
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
                    _state = _state with
                    {
                        PriorityLocations = _state.PriorityLocations.Add(new()
                        {
                            Location = toPrioritize,
                            Source = PriorityLocationModel.SourceKind.Player,
                        }),
                    };
                    await SaveAsync();
                }
                finally
                {
                    _gameStateMutex.Release();
                }

                SayPacketModel say = new()
                {
                    Text = $"Confirm prioritizing '{toPrioritize.Name}' for user '{probablyPlayerAlias}'",
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
            PriorityLocationModel? toRemove = null;
            await _gameStateMutex.WaitAsync();
            try
            {
                toRemove = _state.PriorityLocations.FirstOrDefault(l =>
                    l.Source == PriorityLocationModel.SourceKind.Player &&
                    l.Location.Name.Equals(loc, StringComparison.InvariantCultureIgnoreCase)
                );

                if (toRemove is not null)
                {
                    _state = _state with { PriorityLocations = _state.PriorityLocations.Remove(toRemove) };
                    await SaveAsync();
                }
            }
            finally
            {
                _gameStateMutex.Release();
            }

            SayPacketModel say = new()
            {
                Text = toRemove is null
                    ? $"Um... excuse me, but... I don't see a '{loc}' to remove..."
                    : $"Confirm deprioritizing '{toRemove.Location.Name}' for user '{probablyPlayerAlias}'",
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
        await LoadAsync();
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

    private async Task RunPlayLoopAsync()
    {
        _nextFullInterval = NextInterval(_state);
        await _dataAvailableSignal.WaitAsync();
        while (!_state.IsCompleted)
        {
            TimeSpan remaining = _nextFullInterval - _timeProvider.GetElapsedTime(_prevStartTimestamp);
            long dueTime = _timeProvider.GetTimestamp() + ((long)(_nextFullInterval.TotalSeconds * _timeProvider.TimestampFrequency));
            while (_timeProvider.GetTimestamp() < dueTime)
            {
                try
                {
                    await Task.Delay(remaining, _timeProvider, _pauseCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // user clicked Pause
                    long waitStart = _timeProvider.GetTimestamp();
                    TaskCompletionSource cancelTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    await using CancellationTokenRegistration reg = _playCts.Token.Register(() => cancelTcs.TrySetResult());
                    await cancelTcs.Task;
                    dueTime += _timeProvider.GetTimestamp() - waitStart;
                    _playCts = new();
                    _pauseCts = new();
                }
            }

            GameState prevState, nextState;
            _prevStartTimestamp = _timeProvider.GetTimestamp();
            await _gameStateMutex.WaitAsync();
            try
            {
                prevState = _state;
                _nextFullInterval = NextInterval(_state);
                _state = nextState = _player.Advance(prevState);

                if (prevState.Epoch != nextState.Epoch)
                {
                    await SaveAsync();
                }
            }
            finally
            {
                _gameStateMutex.Release();
            }

            if (nextState.CheckedLocations.Count == prevState.CheckedLocations.Count)
            {
                continue;
            }

            List<long> locationIds = [];
            foreach (LocationDefinitionModel location in nextState.CheckedLocations.Except(prevState.CheckedLocations))
            {
                locationIds.Add(_lastFullData.LocationIds[location]);
                if (_landmarkRegionsByLocation.TryGetValue(location, out LandmarkRegionViewModel? viewModel))
                {
                    viewModel.Checked = true;
                }
            }

            LocationChecksPacketModel locationChecks = new() { Locations = locationIds.ToArray() };
            await SendPacketsAsync([locationChecks]);
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
            GeneralItemNameMapping = _dataPackage.Data.Games.Values.SelectMany(game => game.ItemNameToId).ToFrozenDictionary(kvp => kvp.Value, kvp => kvp.Key),
            GeneralLocationNameMapping = _dataPackage.Data.Games.Values.SelectMany(game => game.LocationNameToId).ToFrozenDictionary(kvp => kvp.Value, kvp => kvp.Key),
            SlotInfo = (_lastRoomUpdate?.SlotInfo ?? _connected.SlotInfo).ToFrozenDictionary(),
            LocationIds = gameData.LocationNameToId.ToFrozenDictionary(kvp => GameDefinitions.Instance.LocationsByName[kvp.Key], kvp => kvp.Value),
            ItemsById = gameData.ItemNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.ItemsByName[kvp.Key]),
            LocationsById = gameData.LocationNameToId.ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Instance.LocationsByName[kvp.Key]),
            SlotByPlayerAlias = (_lastRoomUpdate?.Players ?? _connected.Players).ToFrozenDictionary(p => p.Alias, p => p.Slot),
        };
    }

    private void Handle(ref GameState state, ReceivedItemsPacketModel receivedItems)
    {
        var convertedItems = ImmutableArray.CreateRange(receivedItems.Items, (item, itemsReverseMapping) => itemsReverseMapping[item.Item], _lastFullData.ItemsById);
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
        int startledMod = 0;
        List<PriorityLocationModel.SourceKind> smartAndConspiratorial = [];
        foreach (ItemDefinitionModel newItem in newItems)
        {
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
                    case "startled" when state.HasConfidence:
                    case "conspiratorial" when state.HasConfidence:
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

                    case "startled":
                        ++startledMod;
                        break;

                    case "smart":
                        smartAndConspiratorial.Add(PriorityLocationModel.SourceKind.Smart);
                        break;

                    case "conspiratorial":
                        smartAndConspiratorial.Add(PriorityLocationModel.SourceKind.Conspiratorial);
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

        state = state
            .AddStartled(startledMod)
            .ResolveSmartAndConspiratorialAuras(CollectionsMarshal.AsSpan(smartAndConspiratorial), _spoilerData) with
            {
                ReceivedItems = state.ReceivedItems.AddRange(newItems),
                FoodFactor = state.FoodFactor + (foodMod * 5),
                EnergyFactor = state.EnergyFactor + (energyFactorMod * 5),
                LuckFactor = state.LuckFactor + luckFactorMod,
                StyleFactor = state.StyleFactor + (stylishMod * 2),
                DistractionCounter = state.DistractionCounter + distractedMod,
            };

        UpdateMeters();
    }

    private TimeSpan NextInterval(GameState state)
    {
        double rangeSeconds = (double)(_settings.MaxStepSeconds - _settings.MinStepSeconds);
        double baseInterval = (double)_settings.MinStepSeconds + (rangeSeconds * Prng.NextDouble(ref _intervalPrngState));
        return TimeSpan.FromSeconds(baseInterval * state.IntervalDurationMultiplier);
    }

    private void UpdateMeters()
    {
        CurrentLocation = _state.CurrentLocation;

        foreach (ItemDefinitionModel item in _state.ReceivedItems)
        {
            if (_collectableItemsByModel.TryGetValue(item, out CollectableItemViewModel? viewModel))
            {
                viewModel.Collected = true;
            }
        }

        RatCount = _state.RatCount;
        FoodFactor = _state.FoodFactor;
        EnergyFactor = _state.EnergyFactor;
        LuckFactor = _state.LuckFactor;
        StyleFactor = _state.StyleFactor;
        DistractionCounter = _state.DistractionCounter;
        StartledCounter = _state.StartledCounter;
        HasConfidence = _state.HasConfidence;
    }

    private async ValueTask LoadAsync()
    {
        try
        {
            await using FileStream rawStream = _stateFile.Open(s_readOptions);
            await using DecompressionStream stream = new(rawStream);
            GameState.GameStateProxy proxy = (await JsonSerializer.DeserializeAsync(stream, s_gameStateProxyTypeInfo))!;
            _state = proxy.ToState();
            UpdateMeters();
        }
        catch (IOException)
        {
        }
    }

    private async ValueTask SaveAsync()
    {
        UpdateMeters();
        try
        {
            await using (FileStream rawStream = _stateTmpFile.Open(s_writeOptions))
            {
                await using CompressionStream stream = new(rawStream, level: 10, leaveOpen: true);
                await JsonSerializer.SerializeAsync(stream, _state.ToProxy(), s_gameStateProxyTypeInfo);
            }

            _stateTmpFile.MoveTo(_stateFile.FullName, overwrite: true);
        }
        catch (IOException)
        {
        }
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
        public required FrozenDictionary<long, string> GeneralItemNameMapping { get; init; }

        public required FrozenDictionary<long, string> GeneralLocationNameMapping { get; init; }

        public required FrozenDictionary<int, SlotModel> SlotInfo { get; init; }

        public required FrozenDictionary<LocationDefinitionModel, long> LocationIds { get; init; }

        public required FrozenDictionary<long, ItemDefinitionModel> ItemsById { get; init; }

        public required FrozenDictionary<long, LocationDefinitionModel> LocationsById { get; init; }

        public required FrozenDictionary<string, int> SlotByPlayerAlias { get; init; }
    }
}
