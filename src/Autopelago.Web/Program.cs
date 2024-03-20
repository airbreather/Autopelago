using System.Reactive.Concurrency;
using System.Reactive.Linq;

using ArchipelagoClientDotNet;

using Autopelago.Web;

await Helper.ConfigureAwaitFalse();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

builder.Services.AddSingleton<IScheduler>(DefaultScheduler.Instance);
builder.Services.AddSingleton<SlotGameStates>();

builder.Services.AddHostedService<AutopelagoGameService>();

builder.Services.AddControllers();

WebApplication app = builder.Build();

app.UseFileServer();
app.MapHub<GameStateHub>("/gameStateHub");

await app.RunAsync();
