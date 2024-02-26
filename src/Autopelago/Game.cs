using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Threading.Channels;

using ArchipelagoClientDotNet;

public sealed class Game
{
    public sealed record State
    {
        public ulong Epoch { get; init; }

        public int RatCount { get; init; }

        public Prng.State PrngState { get; init; }
    }

    private readonly Channel<ImmutableArray<ArchipelagoPacketModel>, ArchipelagoPacketModel> _channel;

    private bool _startedHandshake;

    private RoomInfoPacketModel? _roomInfo;

    private DataPackagePacketModel? _dataPackage;

    private GameDataModel? _gameData;

    private FrozenDictionary<long, ItemDefinitionModel>? _idToItem;

    private FrozenDictionary<long, LocationDefinitionModel>? _idToLocation;

    private ConnectResponsePacketModel? _lastHandshakeResponse;

    private State _state;

    public Game(Channel<ImmutableArray<ArchipelagoPacketModel>, ArchipelagoPacketModel> channel)
    {
        _channel = channel;
        _state = new() { PrngState = Prng.State.Start(new Random(123)) };
    }

    public async ValueTask StartHandshakeAsync(GetDataPackagePacketModel getDataPackage, CancellationToken cancellationToken = default)
    {
        if (_startedHandshake)
        {
            throw new InvalidOperationException("Previous handshake attempt threw an exception, so this game will never be able to start.");
        }

        _startedHandshake = true;
        _roomInfo = await _channel.Reader.ReadAsync(cancellationToken) as RoomInfoPacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");

        await _channel.Writer.WriteAsync([getDataPackage], cancellationToken);
        _dataPackage = await _channel.Reader.ReadAsync(cancellationToken) as DataPackagePacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");

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

        await _channel.Writer.WriteAsync([connect], cancellationToken);
        _lastHandshakeResponse = await _channel.Reader.ReadAsync(cancellationToken) as ConnectResponsePacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
        return _lastHandshakeResponse is ConnectedPacketModel;
    }

    public async ValueTask RunUntilCanceled(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        if (_lastHandshakeResponse is not ConnectedPacketModel)
        {
            throw new InvalidOperationException("Must finish the handshake successfully first.");
        }

        using SemaphoreSlim mutex = new(1, 1);
        Task readerTask = Task.Run(async () =>
        {
            await Helper.ConfigureAwaitFalse();
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                await mutex.WaitAsync(cancellationToken);
                try
                {
                    while (_channel.Reader.TryRead(out ArchipelagoPacketModel? packet))
                    {
                        if (packet is PrintJSONPacketModel printJSON)
                        {
                            foreach (JSONMessagePartModel part in printJSON.Data)
                            {
                                Console.Write(part.Text);
                            }

                            Console.WriteLine();
                        }
                    }
                }
                finally
                {
                    mutex.Release();
                }
            }
        }, cancellationToken);

        try
        {
            while (true)
            {
                // TODO: better
                await Task.Delay(1000, cancellationToken);

                await mutex.WaitAsync(cancellationToken);
                try
                {
                    State state = Advance();

                    // TODO: send packets as appropriate for how the state has changed from _state
                    _state = state;
                }
                finally
                {
                    mutex.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
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
