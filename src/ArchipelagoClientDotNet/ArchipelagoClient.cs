using System.Collections.Immutable;
using System.Threading.Channels;

namespace ArchipelagoClientDotNet;

public readonly record struct PacketReceivedEventArgs
{
    public required ArchipelagoPacketModel Packet { get; init; }
}

public sealed class ArchipelagoClient
{
    private readonly Channel<ImmutableArray<ArchipelagoPacketModel>, ArchipelagoPacketModel> _channel;

    private readonly AsyncEvent<PacketReceivedEventArgs> _packetReceivedEvent = new();

    private readonly TaskCompletionSource<RoomInfoPacketModel> _roomInfoBox = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _running;

    private bool _gotRoomInfo;

    private DataPackagePacketModel? _dataPackage;

    private ConnectResponsePacketModel? _lastHandshakeResponse;

    public ArchipelagoClient(Channel<ImmutableArray<ArchipelagoPacketModel>, ArchipelagoPacketModel> channel)
    {
        _channel = channel;

        PacketReceived += OnFirstPacketReceived;
        ValueTask OnFirstPacketReceived(object? sender, PacketReceivedEventArgs args, CancellationToken cancellationToken)
        {
            PacketReceived -= OnFirstPacketReceived;
            if (args.Packet is RoomInfoPacketModel roomInfo)
            {
                _roomInfoBox.TrySetResult(roomInfo);
            }
            else
            {
                _roomInfoBox.TrySetException(new InvalidDataException("Server does not properly implement the Archipelago handshake protocol."));
            }

            return ValueTask.CompletedTask;
        }
    }

    public event AsyncEventHandler<PacketReceivedEventArgs> PacketReceived
    {
        add => _packetReceivedEvent.Add(value);
        remove => _packetReceivedEvent.Remove(value);
    }

    public void RunInBackgroundUntilCanceled(CancellationToken cancellationToken)
    {
        if (_running)
        {
            throw new InvalidOperationException("Already running");
        }

        _running = true;
        _ = BackgroundTaskRunner.Run(async () =>
        {
            await Helper.ConfigureAwaitFalse();

            try
            {
                while (await _channel.Reader.WaitToReadAsync(cancellationToken))
                {
                    while (_channel.Reader.TryRead(out ArchipelagoPacketModel? packet))
                    {
                        await _packetReceivedEvent.InvokeAsync(this, new() { Packet = packet }, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cancellationToken);
    }

    public async ValueTask<RoomInfoPacketModel> Handshake1Async(CancellationToken cancellationToken)
    {
        if (!_running)
        {
            throw new InvalidOperationException("Start running first.");
        }

        if (_gotRoomInfo)
        {
            throw new InvalidOperationException("Already completed Handshake1.");
        }

        await Helper.ConfigureAwaitFalse();

        RoomInfoPacketModel roomInfo = await _roomInfoBox.Task.WaitAsync(cancellationToken);
        _gotRoomInfo = true;
        return roomInfo;
    }

    public async ValueTask<DataPackagePacketModel> Handshake2Async(GetDataPackagePacketModel getDataPackage, CancellationToken cancellationToken)
    {
        if (_roomInfoBox is null)
        {
            throw new InvalidOperationException("Finish Handshake1 first.");
        }

        if (_dataPackage is not null)
        {
            throw new InvalidOperationException("Already completed Handshake2.");
        }

        await Helper.ConfigureAwaitFalse();

        TaskCompletionSource<DataPackagePacketModel> dataPackageBox = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PacketReceived += OnNextPacketReceived;
        ValueTask OnNextPacketReceived(object? sender, PacketReceivedEventArgs args, CancellationToken cancellationToken)
        {
            PacketReceived -= OnNextPacketReceived;
            if (args.Packet is DataPackagePacketModel dataPackage)
            {
                dataPackageBox.TrySetResult(dataPackage);
            }
            else
            {
                dataPackageBox.TrySetException(new InvalidDataException("Server does not properly implement the Archipelago handshake protocol."));
            }

            return ValueTask.CompletedTask;
        }

        await WriteNextPacketAsync(getDataPackage, cancellationToken);
        return _dataPackage = await dataPackageBox.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask<ConnectResponsePacketModel> Handshake3Async(ConnectPacketModel connect, CancellationToken cancellationToken)
    {
        if (_dataPackage is null)
        {
            throw new InvalidOperationException("Finish Handshake2 first (it's required here, even though it's not required by the protocol).");
        }

        if (_lastHandshakeResponse is ConnectedPacketModel)
        {
            throw new InvalidOperationException("Already finished the handshake.");
        }

        await Helper.ConfigureAwaitFalse();

        TaskCompletionSource<ConnectResponsePacketModel> connectResponseBox = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PacketReceived += OnNextPacketReceived;
        ValueTask OnNextPacketReceived(object? sender, PacketReceivedEventArgs args, CancellationToken cancellationToken)
        {
            PacketReceived -= OnNextPacketReceived;
            if (args.Packet is ConnectResponsePacketModel connectResponse)
            {
                connectResponseBox.TrySetResult(connectResponse);
            }
            else
            {
                connectResponseBox.TrySetException(new InvalidDataException("Server does not properly implement the Archipelago handshake protocol."));
            }

            return ValueTask.CompletedTask;
        }

        await WriteNextPacketAsync(connect, cancellationToken);
        return _lastHandshakeResponse = await connectResponseBox.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask<RetrievedPacketModel> GetAsync(GetPacketModel getPacket, CancellationToken cancellationToken)
    {
        if (_lastHandshakeResponse is not ConnectedPacketModel)
        {
            throw new InvalidOperationException("Finish Handshake3 successfully first.");
        }

        await Helper.ConfigureAwaitFalse();

        TaskCompletionSource<RetrievedPacketModel> retrievedBox = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PacketReceived += OnClientPacketReceived;
        await WriteNextPacketAsync(getPacket, cancellationToken);
        return await retrievedBox.Task.ConfigureAwait(false);
        ValueTask OnClientPacketReceived(object? sender, PacketReceivedEventArgs args, CancellationToken cancellationToken)
        {
            if (args.Packet is RetrievedPacketModel retrieved)
            {
                PacketReceived -= OnClientPacketReceived;
                retrievedBox.TrySetResult(retrieved);
            }

            return ValueTask.CompletedTask;
        }
    }

    public async ValueTask<SetReplyPacketModel?> SetAsync(SetPacketModel setPacket, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        if (setPacket.WantReply)
        {
            await WriteNextPacketAsync(setPacket, cancellationToken);
            return null;
        }

        TaskCompletionSource<SetReplyPacketModel> setReplyBox = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PacketReceived += OnClientPacketReceived;
        await WriteNextPacketAsync(setPacket, cancellationToken);
        return await setReplyBox.Task.ConfigureAwait(false);
        ValueTask OnClientPacketReceived(object? sender, PacketReceivedEventArgs args, CancellationToken cancellationToken)
        {
            if (args.Packet is SetReplyPacketModel retrieved)
            {
                setReplyBox.TrySetResult(retrieved);
            }

            return ValueTask.CompletedTask;
        }
    }

    public ValueTask WriteNextPacketAsync(ArchipelagoPacketModel nextPacket, CancellationToken cancellationToken)
    {
        return WriteNextPacketsAsync([nextPacket], cancellationToken);
    }

    public ValueTask WriteNextPacketsAsync(ImmutableArray<ArchipelagoPacketModel> nextPackets, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(nextPackets, cancellationToken);
    }
}
