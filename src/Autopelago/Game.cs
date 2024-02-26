using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

using ArchipelagoClientDotNet;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Game.State), TypeInfoPropertyName = "Game")]
internal sealed partial class SourceGenerationContext : JsonSerializerContext
{
}

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Lifetime is too close to the application's lifetime for me to care right now.")]
public sealed class Game
{
    public sealed record State
    {
        public ulong Epoch { get; init; }

        public int RatCount => ReceivedItems.Sum(i => i.RatCount).GetValueOrDefault();

        public ImmutableList<ItemDefinitionModel> ReceivedItems { get; init; } = [];

        public Prng.State PrngState { get; init; }
    }

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        TypeInfoResolver = SourceGenerationContext.Default,
    };

    private readonly IArchipelagoClient _client;

    private readonly TimeProvider _timeProvider;

    private readonly SemaphoreSlim _mutex = new(1, 1);

    private bool _startedHandshake;

    private RoomInfoPacketModel? _roomInfo;

    private DataPackagePacketModel? _dataPackage;

    private GameDataModel? _gameData;

    private FrozenDictionary<long, ItemDefinitionModel>? _idToItem;

    private FrozenDictionary<long, LocationDefinitionModel>? _idToLocation;

    private ConnectResponsePacketModel? _lastHandshakeResponse;

    private string? _gameStateKey;

    private State _state;

    public Game(IArchipelagoClient client, TimeProvider timeProvider)
    {
        _client = client;
        _timeProvider = timeProvider;
        _state = new() { PrngState = Prng.State.Start(new Random(123)) };
    }

    public async ValueTask StartHandshakeAsync(GetDataPackagePacketModel getDataPackage, CancellationToken cancellationToken = default)
    {
        if (_startedHandshake)
        {
            throw new InvalidOperationException("Previous handshake attempt threw an exception, so this game will never be able to start.");
        }

        _startedHandshake = true;
        _roomInfo = await _client.ReadNextPacketAsync(cancellationToken) as RoomInfoPacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");

        await _client.WriteNextPacketAsync(getDataPackage, cancellationToken);
        _dataPackage = await _client.ReadNextPacketAsync(cancellationToken) as DataPackagePacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");

        _gameData = _dataPackage.Data.Games["Autopelago"];

        _idToItem = _gameData.ItemNameToId.Where(kvp => GameDefinitions.Items.ContainsKey(kvp.Key)).ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Items[kvp.Key]);
        _idToLocation = _gameData.LocationNameToId.Where(kvp => GameDefinitions.Locations.ContainsKey(kvp.Key)).ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Locations[kvp.Key]);
    }

    public async ValueTask<bool> FinishHandshakeAsync(ConnectPacketModel connect, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();

        if (_idToLocation is null) // ideally, observe the last thing that happens in Start
        {
            throw new InvalidOperationException("Start the handshake first.");
        }

        if (_lastHandshakeResponse is ConnectedPacketModel)
        {
            throw new InvalidOperationException("Already finished the handshake.");
        }

        await _client.WriteNextPacketAsync(connect, cancellationToken);
        _lastHandshakeResponse = await _client.ReadNextPacketAsync(cancellationToken) as ConnectResponsePacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
        return _lastHandshakeResponse is ConnectedPacketModel;
    }

    public async ValueTask RunUntilCanceled(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        if (_lastHandshakeResponse is not ConnectedPacketModel connected)
        {
            throw new InvalidOperationException("Must finish the handshake successfully first.");
        }

        _gameStateKey = $"autopelago_state_{connected.Team}_{connected.Slot}";
        GetPacketModel retrieveGameState = new() { Keys = [_gameStateKey] };
        RetrievedPacketModel retrievedGameState = await _client.GetAsync(retrieveGameState, cancellationToken);

        if (retrievedGameState.Keys.TryGetValue(_gameStateKey, out JsonElement stateElement) && JsonSerializer.Deserialize<State>(stateElement, s_jsonSerializerOptions) is State serializedState)
        {
            _state = serializedState;
        }

        _ = Task.Run(async () => await ProcessIncomingPacketsAsync(cancellationToken), cancellationToken);

        while (true)
        {
            // TODO: better
            await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken);

            await _mutex.WaitAsync(cancellationToken);
            try
            {
                State state = Advance();

                // TODO: send packets as appropriate for how the state has changed from _state
                _state = state;
            }
            finally
            {
                _mutex.Release();
            }
        }
    }

    private async ValueTask ProcessIncomingPacketsAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        try
        {
            while (true)
            {
                switch (await _client.ReadNextPacketAsync(cancellationToken))
                {
                    case PrintJSONPacketModel printJSON: Dispatch(printJSON); break;
                    case ReceivedItemsPacketModel receivedItems: Dispatch(receivedItems); break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Dispatch(PrintJSONPacketModel printJSON)
    {
        foreach (JSONMessagePartModel part in printJSON.Data)
        {
            Console.Write(part.Text);
        }

        Console.WriteLine();
    }

    private void Dispatch(ReceivedItemsPacketModel receivedItems)
    {
        _state = _state with
        {
            Epoch = _state.Epoch + 1,
            ReceivedItems = _state.ReceivedItems.AddRange(receivedItems.Items.Where(i => _idToItem!.ContainsKey(i.Item)).Select(i => _idToItem![i.Item])),
        };
    }

    private State Advance()
    {
        // TODO: actually everything here.
        return _state with
        {
            Epoch = _state.Epoch + 1,
        };
    }
}
