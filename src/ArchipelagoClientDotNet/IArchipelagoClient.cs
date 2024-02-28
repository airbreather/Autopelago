using System.Collections.Immutable;

namespace ArchipelagoClientDotNet;

public readonly record struct PacketReceivedEventArgs
{
    public required ArchipelagoPacketModel Packet { get; init; }
}

public interface IArchipelagoClient
{
    event AsyncEventHandler<PacketReceivedEventArgs> PacketReceived;

    ValueTask<RoomInfoPacketModel> Handshake1Async(CancellationToken cancellationToken);

    ValueTask<DataPackagePacketModel> Handshake2Async(GetDataPackagePacketModel getDataPackage, CancellationToken cancellationToken);

    ValueTask<ConnectResponsePacketModel> Handshake3Async(ConnectPacketModel connect, CancellationToken cancellationToken);

    ValueTask RunUntilCanceledAsync(CancellationToken cancellationToken);

    ValueTask WriteNextPacketAsync(ArchipelagoPacketModel nextPacket, CancellationToken cancellationToken) => WriteNextPacketsAsync([nextPacket], cancellationToken);

    ValueTask WriteNextPacketsAsync(ImmutableArray<ArchipelagoPacketModel> nextPackets, CancellationToken cancellationToken);
}
