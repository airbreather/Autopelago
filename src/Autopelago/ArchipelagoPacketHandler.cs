namespace Autopelago;

public abstract class ArchipelagoPacketHandler
{
    public abstract ValueTask HandleAsync(ArchipelagoPacketModel nextPacket, ArchipelagoPacketProvider sender, CancellationToken cancellationToken);
}
