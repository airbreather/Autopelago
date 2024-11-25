using System.Net.WebSockets;

namespace Autopelago;

public sealed class ArchipelagoConnection
{
    private readonly Settings _settings;

    public ArchipelagoConnection(Settings settings)
    {
        _settings = settings;
    }

    public async ValueTask<ClientWebSocket> ConnectAsync(CancellationToken cancellationToken)
    {
        ClientWebSocket socket = new() { Options = { DangerousDeflateOptions = new() } };
        Exception originalException;
        try
        {
            await socket.ConnectAsync(new($"wss://{_settings.Host}:{_settings.Port}"), cancellationToken);
            return socket;
        }
        catch (Exception ex)
        {
            // the socket actually disposes itself after ConnectAsync fails for practically any
            // reason (which is why we need to overwrite it with a new one here), but it still makes
            // me feel icky not to dispose it explicitly before overwriting it, so just do it
            // ourselves (airbreather 2024-01-12).
            socket.Dispose();
            originalException = ex;
        }

        try
        {
            socket = new() { Options = { DangerousDeflateOptions = new() } };
            await socket.ConnectAsync(new($"ws://{_settings.Host}:{_settings.Port}"), cancellationToken);
            return socket;
        }
        catch (Exception ex2)
        {
            throw new AggregateException(originalException, ex2);
        }
    }
}
