using System.Drawing;

using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.MessageLog.Parts;
using Archipelago.MultiClient.Net.Packets;

using Console = Colorful.Console;

namespace Autopelago;

public sealed class AutopelagoGame
{
    private static readonly Version s_minArchipelagoVersion = new("0.4.3");

    private ArchipelagoSession? _archipelagoSession;

    public async ValueTask StartAsync(string server, ushort port, string slot, string? password = null)
    {
        await Helper.ConfigureAwaitFalse();

        _archipelagoSession = ArchipelagoSessionFactory.CreateSession(server, port);
        _archipelagoSession.MessageLog.OnMessageReceived += OnMessageReceived;
        RoomInfoPacket roomInfo = await _archipelagoSession.ConnectAsync();

        LoginResult loginResult = await _archipelagoSession.LoginAsync(
            game: "Autopelago",
            name: slot,
            itemsHandlingFlags: ItemsHandlingFlags.AllItems,
            version: s_minArchipelagoVersion,
            password: password);
        if (!loginResult.Successful)
        {
            throw new LoginFailedException();
        }
    }

    private void OnMessageReceived(LogMessage message)
    {
        foreach (MessagePart part in message.Parts)
        {
            string messageText = part.Type switch
            {
                MessagePartType.Text => part.Text,
                MessagePartType other => $"({part.Type}): {part.Text}",
            };
            if (part.IsBackgroundColor)
            {
                Console.WriteLine(messageText);
            }
            else
            {
                Console.WriteLine(messageText, Color.FromArgb(255, part.Color.R, part.Color.G, part.Color.B));
            }
        }
    }
}
