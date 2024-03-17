using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.ExceptionServices;

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

app.UseStaticFiles();
app.MapHub<GameStateHub>("/gameStateHub");

ExceptionDispatchInfo? edi = null;
BackgroundTaskRunner.BackgroundException += (sender, args) =>
{
    if (args.BackgroundException.SourceException is not OperationCanceledException)
    {
        edi = args.BackgroundException;
        app.Lifetime.StopApplication();
    }
};

await app.RunAsync();
edi?.Throw();
