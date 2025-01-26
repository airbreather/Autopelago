namespace Autopelago;

public sealed class UserInitiatedActions
{
    private readonly ArchipelagoPacketProvider _packets;

    public UserInitiatedActions(ArchipelagoPacketProvider packets)
    {
        _packets = packets;
    }

    public async ValueTask RequestItemHintAsync(ItemKey item)
    {
        await _packets.SendPacketsAsync([new SayPacketModel
        {
            Text = $"!hint {GameDefinitions.Instance[item].Name}",
        }]);
    }
}
