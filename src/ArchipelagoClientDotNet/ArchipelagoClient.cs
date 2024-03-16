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

    private RoomInfoPacketModel? _roomInfo;

    private DataPackagePacketModel? _dataPackage;

    private ConnectResponsePacketModel? _lastHandshakeResponse;

    public ArchipelagoClient(Channel<ImmutableArray<ArchipelagoPacketModel>, ArchipelagoPacketModel> channel)
    {
        _channel = channel;
    }

    public event AsyncEventHandler<PacketReceivedEventArgs> PacketReceived
    {
        add => _packetReceivedEvent.Add(value);
        remove => _packetReceivedEvent.Remove(value);
    }

    public async ValueTask<RoomInfoPacketModel> Handshake1Async(CancellationToken cancellationToken)
    {
        if (_roomInfo is not null)
        {
            throw new InvalidOperationException("Already completed Handshake1.");
        }

        await Helper.ConfigureAwaitFalse();

        _roomInfo = await _channel.Reader.ReadAsync(cancellationToken) as RoomInfoPacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
        await _packetReceivedEvent.InvokeAsync(this, new() { Packet = _roomInfo }, cancellationToken);
        return _roomInfo;
    }

    public async ValueTask<DataPackagePacketModel> Handshake2Async(GetDataPackagePacketModel getDataPackage, CancellationToken cancellationToken)
    {
        if (_roomInfo is null)
        {
            throw new InvalidOperationException("Finish Handshake1 first.");
        }

        if (_dataPackage is not null)
        {
            throw new InvalidOperationException("Already completed Handshake2.");
        }

        await Helper.ConfigureAwaitFalse();

        await WriteNextPacketAsync(getDataPackage, cancellationToken);
        _dataPackage = await _channel.Reader.ReadAsync(cancellationToken) as DataPackagePacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
        await _packetReceivedEvent.InvokeAsync(this, new() { Packet = _dataPackage }, cancellationToken);
        return _dataPackage;
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

        await WriteNextPacketAsync(connect, cancellationToken);
        _lastHandshakeResponse = await _channel.Reader.ReadAsync(cancellationToken) as ConnectResponsePacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
        await _packetReceivedEvent.InvokeAsync(this, new() { Packet = _lastHandshakeResponse }, cancellationToken);
        return _lastHandshakeResponse;
    }

    public async ValueTask RunUntilCanceledAsync(CancellationToken cancellationToken)
    {
        if (_lastHandshakeResponse is not ConnectedPacketModel)
        {
            throw new InvalidOperationException("Finish Handshake3 successfully first.");
        }

        await Helper.ConfigureAwaitFalse();

        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_channel.Reader.TryRead(out ArchipelagoPacketModel? packet))
            {
                await _packetReceivedEvent.InvokeAsync(this, new() { Packet = packet }, cancellationToken);
            }
        }
    }

    public async ValueTask<RetrievedPacketModel> GetAsync(GetPacketModel getPacket, CancellationToken cancellationToken)
    {
        if (_lastHandshakeResponse is not ConnectedPacketModel)
        {
            throw new InvalidOperationException("Finish Handshake3 successfully first.");
        }

        await Helper.ConfigureAwaitFalse();

        TaskCompletionSource<RetrievedPacketModel> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PacketReceived += OnClientPacketReceived;
        await WriteNextPacketAsync(getPacket, cancellationToken);
        return await tcs.Task.ConfigureAwait(false);
        ValueTask OnClientPacketReceived(object? sender, PacketReceivedEventArgs args, CancellationToken cancellationToken)
        {
            if (args.Packet is RetrievedPacketModel retrieved)
            {
                PacketReceived -= OnClientPacketReceived;
                tcs.TrySetResult(retrieved);
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

        TaskCompletionSource<SetReplyPacketModel> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PacketReceived += OnClientPacketReceived;
        await WriteNextPacketAsync(setPacket, cancellationToken);
        return await tcs.Task.ConfigureAwait(false);
        ValueTask OnClientPacketReceived(object? sender, PacketReceivedEventArgs args, CancellationToken cancellationToken)
        {
            if (args.Packet is SetReplyPacketModel retrieved)
            {
                tcs.TrySetResult(retrieved);
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
