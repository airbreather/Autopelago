namespace Autopelago;

public sealed class UserInitiatedActions
{
    private readonly ArchipelagoPacketProvider _packets;

    public UserInitiatedActions(ArchipelagoPacketProvider packets)
    {
        _packets = packets;
    }

    public async ValueTask RequestItemHintAsync(ItemKey item, bool lactoseIntolerant)
    {
        string name = lactoseIntolerant
            ? GameDefinitions.Instance[item].LactoseIntolerantName
            : GameDefinitions.Instance[item].NormalName;
        await _packets.SendPacketsAsync([new SayPacketModel
        {
            Text = $"!hint {name}",
        }]);
    }
}
