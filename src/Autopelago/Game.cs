using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using ArchipelagoClientDotNet;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Lifetime is too close to the application's lifetime for me to care right now.")]
public sealed class Game
{
    public sealed record State
    {
        public ulong Epoch { get; init; }

        public int RatCount => ReceivedItems.Sum(i => i.RatCount).GetValueOrDefault();

        public ImmutableList<ItemDefinitionModel> ReceivedItems { get; init; } = [];

        public Prng.State PrngState { get; init; }

        public Proxy ToProxy()
        {
            return new()
            {
                Epoch = Epoch,
                ReceivedItems = [..ReceivedItems.Select(i => i.Name)],
                PrngState = PrngState,
            };
        }

        public sealed record Proxy
        {
            public ulong Epoch { get; init; }

            public ImmutableArray<string> ReceivedItems { get; init; }

            public Prng.State PrngState { get; init; }

            public State ToState()
            {
                return new()
                {
                    Epoch = Epoch,
                    ReceivedItems = [..ReceivedItems.Select(name => GameDefinitions.Items[name])],
                    PrngState = PrngState,
                };
            }
        }
    }

    private readonly IArchipelagoClient _client;

    private readonly TimeProvider _timeProvider;

    private readonly SemaphoreSlim _mutex = new(1, 1);

    private RoomInfoPacketModel? _roomInfo;

    private DataPackagePacketModel? _dataPackage;

    private GameDataModel? _gameData;

    private FrozenDictionary<long, ItemDefinitionModel>? _idToItem;

    private FrozenDictionary<long, LocationDefinitionModel>? _idToLocation;

    private ConnectResponsePacketModel? _lastHandshakeResponse;

    private GameStateStorage? _stateStorage;

    private State _state;

    public Game(IArchipelagoClient client, TimeProvider timeProvider)
    {
        _client = client;
        _timeProvider = timeProvider;
        _state = new() { PrngState = Prng.State.Start(new Random(123)) };
    }

    public async ValueTask SetStateStorageAsync(GameStateStorage stateStorage, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        if (_stateStorage is not null)
        {
            throw new InvalidOperationException("Already called this method before.");
        }

        _stateStorage = stateStorage;
        if (await stateStorage.LoadAsync(cancellationToken) is State initialState)
        {
            _state = initialState;
        }
    }

    public async ValueTask<RoomInfoPacketModel> Handshake1Async(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        if (_roomInfo is not null)
        {
            throw new InvalidOperationException("Already completed Handshake1.");
        }

        return _roomInfo = await _client.ReadNextPacketAsync(cancellationToken) as RoomInfoPacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
    }

    public async ValueTask<DataPackagePacketModel> Handshake2Async(GetDataPackagePacketModel getDataPackage, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        await _client.WriteNextPacketAsync(getDataPackage, cancellationToken);
        _dataPackage = await _client.ReadNextPacketAsync(cancellationToken) as DataPackagePacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");

        _gameData = _dataPackage.Data.Games["Autopelago"];

        _idToItem = _gameData.ItemNameToId.Where(kvp => GameDefinitions.Items.ContainsKey(kvp.Key)).ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Items[kvp.Key]);
        _idToLocation = _gameData.LocationNameToId.Where(kvp => GameDefinitions.Locations.ContainsKey(kvp.Key)).ToFrozenDictionary(kvp => kvp.Value, kvp => GameDefinitions.Locations[kvp.Key]);

        return _dataPackage;
    }

    public async ValueTask<ConnectResponsePacketModel> Handshake3Async(ConnectPacketModel connect, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        if (_idToLocation is null) // ideally, observe the last thing that happens in Handshake2
        {
            throw new InvalidOperationException("Finish Handshake2 first (it's required here, even though it's not required by the protocol).");
        }

        if (_lastHandshakeResponse is ConnectedPacketModel)
        {
            throw new InvalidOperationException("Already finished the handshake.");
        }

        await _client.WriteNextPacketAsync(connect, cancellationToken);
        return _lastHandshakeResponse = await _client.ReadNextPacketAsync(cancellationToken) as ConnectResponsePacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
    }

    public async ValueTask RunUntilCanceledAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        if (_stateStorage is null)
        {
            throw new InvalidOperationException("Must set the state storage first.");
        }

        _ = BackgroundTaskRunner.Run(async () => await ProcessIncomingPacketsAsync(cancellationToken), cancellationToken);

        while (true)
        {
            // TODO: better
            await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken);

            await _mutex.WaitAsync(cancellationToken);
            try
            {
                State state = Advance();
                if (_state != state)
                {
                    // TODO: send packets as appropriate for how the state has changed

                    await _stateStorage.SaveAsync(state, cancellationToken);
                    _state = state;
                }
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
        for (int i = receivedItems.Index; i < _state.ReceivedItems.Count; i++)
        {
            if (_state.ReceivedItems[i] != _state.ReceivedItems[i - receivedItems.Index])
            {
                throw new NotImplementedException("Need to resync.");
            }
        }

        if (_state.ReceivedItems.Count - receivedItems.Index == _state.ReceivedItems.Count)
        {
            return;
        }

        _state = _state with
        {
            Epoch = _state.Epoch + 1,
            ReceivedItems = _state.ReceivedItems.AddRange(receivedItems.Items.Skip(_state.ReceivedItems.Count - receivedItems.Index).Where(i => _idToItem!.ContainsKey(i.Item)).Select(i => _idToItem![i.Item])),
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
