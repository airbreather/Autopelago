using System.Collections.Immutable;

namespace ArchipelagoClientDotNet;

public interface IArchipelagoProtocolClient
{
    event AsyncEventHandler<ImmutableArray<ArchipelagoPacketModel>> PacketGroupReceived;

    event AsyncEventHandler<ArchipelagoPacketModel> AnyPacketReceived;

    ValueTask WriteAsync<T>(ImmutableArray<T> values, CancellationToken cancellationToken = default)
        where T : ArchipelagoPacketModel;

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
