using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Reactive.Subjects;

namespace Autopelago;

public sealed class GameStateObservableProvider : IAsyncDisposable
{
    private readonly Player _player = new();

    private readonly CompositeDisposable _disposables = [];

    private readonly TimeProvider _timeProvider;

    private readonly MultiworldInfoEarly _multiworldInfoEarly = new();

    private MultiworldInfo _multiworldInfo = new()
    {
        GeneralItemNameMapping = FrozenDictionary<string, FrozenDictionary<long, string>>.Empty,
        GeneralLocationNameMapping = FrozenDictionary<string, FrozenDictionary<long, string>>.Empty,
        SlotInfo = FrozenDictionary<int, SlotModel>.Empty,
        LocationIds = FrozenDictionary<LocationDefinitionModel, long>.Empty,
        ItemsById = FrozenDictionary<long, ItemDefinitionModel>.Empty,
        LocationsById = FrozenDictionary<long, LocationDefinitionModel>.Empty,
        SlotByPlayerAlias = FrozenDictionary<string, int>.Empty,
        SpoilerData = FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags>.Empty,
    };

    public GameStateObservableProvider(Settings settings, TimeProvider timeProvider)
    {
        MySettings = settings;
        _timeProvider = timeProvider;
        CurrentGameState = new(GameState.Start());
        _disposables.Add(CurrentGameState);
    }

    public Settings MySettings { get; }

    public BehaviorSubject<GameState> CurrentGameState { get; }

    public bool Paused { get; private set; }

    public bool Pause()
    {
        if (Paused)
        {
            return false;
        }

        Paused = true;
        return true;
    }

    public bool Unpause()
    {
        if (!Paused)
        {
            return false;
        }

        Paused = false;
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await Helper.ConfigureAwaitFalse();
        _disposables.Dispose();
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

    private sealed record MultiworldInfoEarly
    {
        public FrozenDictionary<string, FrozenDictionary<long, string>>? GeneralItemNameMapping { get; set; }

        public FrozenDictionary<string, FrozenDictionary<long, string>>? GeneralLocationNameMapping { get; set; }

        public FrozenDictionary<int, SlotModel>? SlotInfo { get; set; }

        public FrozenDictionary<LocationDefinitionModel, long>? LocationIds { get; set; }

        public FrozenDictionary<long, ItemDefinitionModel>? ItemsById { get; set; }

        public FrozenDictionary<long, LocationDefinitionModel>? LocationsById { get; set; }

        public FrozenDictionary<string, int>? SlotByPlayerAlias { get; set; }

        public FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags>? SpoilerData { get; set; }

        public bool TrySeal([NotNullWhen(true)] out MultiworldInfo? result)
        {
            if (GeneralItemNameMapping is not { } generalItemNameMapping ||
                GeneralLocationNameMapping is not { } generalLocationNameMapping ||
                SlotInfo is not { } slotInfo ||
                LocationIds is not { } locationIds ||
                ItemsById is not { } itemsById ||
                LocationsById is not { } locationsById ||
                SlotByPlayerAlias is not { } slotByPlayerAlias ||
                SpoilerData is not { } spoilerData)
            {
                result = null;
                return false;
            }

            result = new()
            {
                GeneralItemNameMapping = generalItemNameMapping,
                GeneralLocationNameMapping = generalLocationNameMapping,
                SlotInfo = slotInfo,
                LocationIds = locationIds,
                ItemsById = itemsById,
                LocationsById = locationsById,
                SlotByPlayerAlias = slotByPlayerAlias,
                SpoilerData = spoilerData,
            };
            return true;
        }
    }

    private sealed record MultiworldInfo
    {
        public required FrozenDictionary<string, FrozenDictionary<long, string>> GeneralItemNameMapping { get; init; }

        public required FrozenDictionary<string, FrozenDictionary<long, string>> GeneralLocationNameMapping { get; init; }

        public required FrozenDictionary<int, SlotModel> SlotInfo { get; init; }

        public required FrozenDictionary<LocationDefinitionModel, long> LocationIds { get; init; }

        public required FrozenDictionary<long, ItemDefinitionModel> ItemsById { get; init; }

        public required FrozenDictionary<long, LocationDefinitionModel> LocationsById { get; init; }

        public required FrozenDictionary<string, int> SlotByPlayerAlias { get; init; }

        public required FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags> SpoilerData { get; init; }
    }
}
