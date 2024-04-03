using System.Runtime.ExceptionServices;

using Autopelago;

using Microsoft.AspNetCore.ResponseCompression;

using Serilog;
using Serilog.Events;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

await Helper.ConfigureAwaitFalse();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Filter.ByExcluding(evt => evt.Exception is OperationCanceledException)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.AddSingleton(sp =>
{
    IHostApplicationLifetime lifetime = sp.GetRequiredService<IHostApplicationLifetime>();

    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "game-config.yaml");
    string settingsYaml = File.ReadAllTextAsync(path, lifetime.ApplicationStopping)
        .GetAwaiter()
        .GetResult();

    return new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build()
        .Deserialize<AutopelagoSettingsModel>(settingsYaml);
});

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.TypeInfoResolver = SignalRSerializerContext.Default;
    });

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SlotGameLookup>();

builder.Services.AddHostedService<AutopelagoGameService>();

builder.Services.AddControllers();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes
        .Append("application/x-font-ttf");
});

WebApplication app = builder.Build();

app.UseSerilogRequestLogging();

app.UseResponseCompression();

app.UseFileServer();
app.MapHub<GameStateHub>("/gameStateHub", options =>
{
    options.AllowStatefulReconnects = true;
});

using CancellationTokenSource cts = new();
SyncOverAsync.BackgroundException += (sender, args) =>
{
    Log.Fatal(args.SourceException, "An unexpected error occurred.");
    cts.Cancel();
};

try
{
    await app.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
}
finally
{
    await Log.CloseAndFlushAsync();
}
