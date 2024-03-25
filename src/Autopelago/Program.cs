using System.Runtime.ExceptionServices;

using Autopelago;

using Microsoft.AspNetCore.ResponseCompression;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

await Helper.ConfigureAwaitFalse();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddSignalR();

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

app.UseResponseCompression();

app.UseFileServer();
app.MapHub<GameStateHub>("/gameStateHub", options =>
{
    options.AllowStatefulReconnects = true;
});

using CancellationTokenSource cts = new();
ExceptionDispatchInfo? thrownException = null;
SyncOverAsync.BackgroundException += (sender, args) =>
{
    thrownException = args;
    cts.Cancel();
};

try
{
    await app.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
}

thrownException?.Throw();
