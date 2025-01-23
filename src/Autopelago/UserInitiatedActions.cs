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
        SayPacketModel say = new()
        {
            Text = $"!hint {GameDefinitions.Instance[item].Name}",
            BypassRatChatMute = true,
        };
        await _packets.SendPacketsAsync([say]);
    }
}
