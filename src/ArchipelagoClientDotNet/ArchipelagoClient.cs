using System.Collections.Immutable;
using System.Threading.Channels;

namespace ArchipelagoClientDotNet;

public sealed class ArchipelagoClient : IArchipelagoClient
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
        await Helper.ConfigureAwaitFalse();

        if (_roomInfo is not null)
        {
            throw new InvalidOperationException("Already completed Handshake1.");
        }

        _roomInfo = await _channel.Reader.ReadAsync(cancellationToken) as RoomInfoPacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
        await _packetReceivedEvent.InvokeAsync(this, new() { Packet = _roomInfo }, cancellationToken);
        return _roomInfo;
    }

    public async ValueTask<DataPackagePacketModel> Handshake2Async(GetDataPackagePacketModel getDataPackage, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        await WriteNextPacketsAsync([getDataPackage], cancellationToken);
        _dataPackage = await _channel.Reader.ReadAsync(cancellationToken) as DataPackagePacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
        await _packetReceivedEvent.InvokeAsync(this, new() { Packet = _dataPackage }, cancellationToken);
        return _dataPackage;
    }

    public async ValueTask<ConnectResponsePacketModel> Handshake3Async(ConnectPacketModel connect, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        if (_dataPackage is null)
        {
            throw new InvalidOperationException("Finish Handshake2 first (it's required here, even though it's not required by the protocol).");
        }

        if (_lastHandshakeResponse is ConnectedPacketModel)
        {
            throw new InvalidOperationException("Already finished the handshake.");
        }

        await WriteNextPacketsAsync([connect], cancellationToken);
        _lastHandshakeResponse = await _channel.Reader.ReadAsync(cancellationToken) as ConnectResponsePacketModel ?? throw new InvalidDataException("Server does not properly implement the Archipelago handshake protocol.");
        await _packetReceivedEvent.InvokeAsync(this, new() { Packet = _lastHandshakeResponse }, cancellationToken);
        return _lastHandshakeResponse;
    }

    public async ValueTask RunUntilCanceledAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        if (_lastHandshakeResponse is not ConnectedPacketModel)
        {
            throw new InvalidOperationException("Finish Handshake3 successfully first.");
        }

        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_channel.Reader.TryRead(out ArchipelagoPacketModel? packet))
            {
                await _packetReceivedEvent.InvokeAsync(this, new() { Packet = packet }, cancellationToken);
            }
        }
    }

    public ValueTask WriteNextPacketsAsync(ImmutableArray<ArchipelagoPacketModel> nextPackets, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(nextPackets, cancellationToken);
    }
}
