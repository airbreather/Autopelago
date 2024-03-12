using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;

using ArchipelagoClientDotNet;

using Autopelago;
using Autopelago.Web;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CurrentGameStates>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHostedService<AutopelagoGameService>();

WebApplication app = builder.Build();

app.UseWebSockets();

app.Use(async (context, next) =>
{
    await Helper.ConfigureAwaitFalse();

    Match match = Regex.Match(context.Request.Path, "/ws/(?<slotName>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    if (!match.Success)
    {
        await next();
        return;
    }

    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    string slotName = match.Groups["slotName"].Value;
    CurrentGameStates currentGameStates = context.RequestServices.GetRequiredService<CurrentGameStates>();
    Game.State? prevState = currentGameStates.Get(slotName);
    using WebSocket ws = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext { DangerousEnableCompression = true });
    byte[] buf = new byte[1];
    while (true)
    {
        WebSocketReceiveResult recv = await ws.ReceiveAsync(buf, context.RequestAborted);
        if (recv.MessageType == WebSocketMessageType.Close)
        {
            break;
        }

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
    }
});

app.Run();
