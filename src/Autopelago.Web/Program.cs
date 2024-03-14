using System.Net.Mime;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Web;
using ArchipelagoClientDotNet;

using Autopelago;
using Autopelago.Web;

using Microsoft.AspNetCore.Mvc;

await Helper.ConfigureAwaitFalse();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CurrentGameStates>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHostedService<AutopelagoGameService>();

builder.Services.AddControllers();

WebApplication app = builder.Build();

app.UseWebSockets();

app.MapControllers();

app.Use(async (context, next) =>
{
    await Helper.ConfigureAwaitFalse();

    if (context.Request.Path != "/ws")
    {
        await next();
        return;
    }

    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    string slotName = HttpUtility.UrlDecode(context.WebSockets.WebSocketRequestedProtocols[0]);
    CurrentGameStates currentGameStates = context.RequestServices.GetRequiredService<CurrentGameStates>();
    Game.State? prevState = currentGameStates.Get(slotName);
    using WebSocket ws = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext { DangerousEnableCompression = true });
    byte[] buf = new byte[1];
    while (true)
    {
        if (prevState is null)
        {
            TaskCompletionSource<Game.State> nextStateTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            currentGameStates.GameStateAdvancedEvent += OnGameStateAdvanced;
            prevState = currentGameStates.Get(slotName);
            ValueTask OnGameStateAdvanced(object? sender, GameStateAdvancedEventArgs args, CancellationToken cancellationToken)
            {
                currentGameStates.GameStateAdvancedEvent -= OnGameStateAdvanced;
                nextStateTcs.TrySetResult(args.StateAfterAdvance);
                return ValueTask.CompletedTask;
            }

            if (prevState is null)
            {
                prevState = await nextStateTcs.Task;
            }
            else
            {
                currentGameStates.GameStateAdvancedEvent -= OnGameStateAdvanced;
            }
        }

        Game.State.Proxy proxy = prevState.ToProxy();
        prevState = null;
        await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(proxy, Game.State.Proxy.SerializerOptions), WebSocketMessageType.Text, true, CancellationToken.None);
        WebSocketReceiveResult recv = await ws.ReceiveAsync(buf, context.RequestAborted);
        if (recv.MessageType == WebSocketMessageType.Close)
        {
            break;
        }
    }
});

await app.RunAsync();

public class Home : ControllerBase
{
    [HttpGet("/state/{slotName}")]
    public FileResult Get(string slotName)
    {
        return File(Encoding.UTF8.GetBytes($$"""
<html>
    <head>
        <script type="text/javascript">
            window.addEventListener('DOMContentLoaded', async function() {
                const webSocket = new WebSocket(`ws://${window.location.host}/ws`, `{{HttpUtility.UrlEncode(slotName).Replace("`", "\\`")}}`);
                webSocket.onmessage = event => {
                    const dser = JSON.parse(event.data);
                    document.getElementById('curr').innerHTML = `
                    <ul>
                        <li>Current location: ${dser.current_location}</li>
                        <li>Target location:  ${dser.target_location}</li>
                    </ul>
                    Received Items
                    <ul>
                        ${dser.received_items.map(item => `<li>${item}</li>`).join('')}
                    </ul>
                    Checked Locations
                    <ul>
                        ${dser.checked_locations.map(loc => `<li>${loc}</li>`).join('')}
                    </ul>
                    `;
                    webSocket.send('1');
                };
            })
        </script>
    </head>
    <body>
        <div id="curr" />
    </body>
</html>
"""), MediaTypeNames.Text.Html);
    }
}
