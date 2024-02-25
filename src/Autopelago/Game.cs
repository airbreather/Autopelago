using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ArchipelagoClientDotNet;

public sealed class Game
{
    public sealed record State
    {
        public ulong Epoch { get; init; }

        public Prng.State PrngState { get; init; }
    }

    private readonly Channel<ImmutableArray<ArchipelagoPacketModel>, ArchipelagoPacketModel> _channel;

    private bool _startedHandshake;

    private ConnectResponsePacketModel? _lastHandshakeResponse;

    private State _state;

    public Game(Channel<ImmutableArray<ArchipelagoPacketModel>, ArchipelagoPacketModel> channel)
    {
        _channel = channel;
        _state = new() { PrngState = Prng.State.Start(new Random(123)) };
    }

    public ArchipelagoPacketEvents PacketEvents { get; } = new();

    public async ValueTask StartHandshakeAsync(GetDataPackagePacketModel getDataPackage, CancellationToken cancellationToken = default)
    {
        if (_startedHandshake)
        {
            throw new InvalidOperationException("Previous handshake attempt threw an exception, so this game will never be able to start.");
        }

        _startedHandshake = true;
        RoomInfoPacketModel roomInfo = await _channel.Reader.ReadAsync(cancellationToken) as RoomInfoPacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
        await NotifyReceivedAsync(roomInfo, cancellationToken);

        await _channel.Writer.WriteAsync([getDataPackage], cancellationToken);
        DataPackagePacketModel dataPackage = await _channel.Reader.ReadAsync(cancellationToken) as DataPackagePacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
        await NotifyReceivedAsync(dataPackage, cancellationToken);
    }

    public async ValueTask<bool> FinishHandshakeAsync(ConnectPacketModel connect, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();

        if (!_startedHandshake)
        {
            throw new InvalidOperationException("Start the handshake first.");
        }

        if (_lastHandshakeResponse is ConnectedPacketModel)
        {
            throw new InvalidOperationException("Already finished the handshake.");
        }

        await _channel.Writer.WriteAsync([connect], cancellationToken);
        _lastHandshakeResponse = await _channel.Reader.ReadAsync(cancellationToken) as ConnectResponsePacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
        await NotifyReceivedAsync(_lastHandshakeResponse, cancellationToken);
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
                        // do stuff with the packet.
                    }
                }
                finally
                {
                    mutex.Release();
                }
            }
        }, cancellationToken);

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

    private State Advance()
    {
        // TODO: actually everything here.
        return _state with
        {
            Epoch = _state.Epoch + 1,
        };
    }

    private async ValueTask NotifyReceivedAsync<T>(T args, CancellationToken cancellationToken)
        where T : ArchipelagoPacketModel
    {
        await Helper.ConfigureAwaitFalse();

        if (typeof(T) != typeof(PrintJSONPacketModel) && typeof(PrintJSONPacketModel).IsAssignableFrom(typeof(T)))
        {
            await NotifyReceivedAsync(Unsafe.As<T, PrintJSONPacketModel>(ref args), cancellationToken);
        }

        if (typeof(T) != typeof(ArchipelagoPacketModel))
        {
            await NotifyReceivedAsync<ArchipelagoPacketModel>(args, cancellationToken);
        }

        await PacketEvents.Received.NotifyAsync(args, cancellationToken);
    }
}
