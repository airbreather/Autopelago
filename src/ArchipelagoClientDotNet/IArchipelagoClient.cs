using System.Collections.Immutable;

namespace ArchipelagoClientDotNet;

public interface IArchipelagoClient
{
    ValueTask<ArchipelagoPacketModel> ReadNextPacketAsync(CancellationToken cancellationToken);

    ValueTask WriteNextPacketAsync(ArchipelagoPacketModel nextPacket, CancellationToken cancellationToken);

    ValueTask WriteNextPacketsAsync(ImmutableArray<ArchipelagoPacketModel> nextPackets, CancellationToken cancellationToken);

    ValueTask<RetrievedPacketModel> GetAsync(GetPacketModel getPacket, CancellationToken cancellationToken);

    ValueTask<SetReplyPacketModel?> SetAsync(SetPacketModel setPacket, CancellationToken cancellationToken);
}
