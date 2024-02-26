using System.Runtime.ExceptionServices;

using ArchipelagoClientDotNet;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

await Helper.ConfigureAwaitFalse();

string settingsYaml = await File.ReadAllTextAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), args[0]));
AutopelagoSettingsModel settings = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .Build()
    .Deserialize<AutopelagoSettingsModel>(settingsYaml);

using CancellationTokenSource cts = new();

ExceptionDispatchInfo? backgroundException = null;
BackgroundTaskRunner.BackgroundException += (sender, args) =>
{
    backgroundException = args.BackgroundException;
    cts.Cancel();
};

Console.CancelKeyPress += async (sender, args) =>
{
    args.Cancel = true;
    await cts.CancelAsync();
};

await using WebSocketPacketChannel channel = new(settings.Server, settings.Port);
await channel.ConnectAsync(cts.Token);
ArchipelagoClient client = new(channel);

Game game = new(client, TimeProvider.System);
try
{
    RoomInfoPacketModel roomInfo = await game.Handshake1Async(cts.Token);
    DataPackagePacketModel dataPackage = await game.Handshake2Async(new() { Games = [settings.GameName] }, cts.Token);
    ConnectResponsePacketModel connectResponse = await game.Handshake3Async(new()
    {
        Password = settings.Slots[0].Password,
        Game = settings.GameName,
        Name = settings.Slots[0].Name,
        Uuid = Guid.NewGuid(),
        Version = new(new Version("0.4.4")),
        ItemsHandling = ArchipelagoItemsHandlingFlags.All,
        Tags = ["AP"],
        SlotData = true,
    }, cts.Token);

    if (connectResponse is not ConnectedPacketModel { Team: int team, Slot: int slot })
    {
        throw new InvalidDataException("Connection refused.");
    }

    //// await game.SetStateStorageAsync(new ArchipelagoGameStateStorage(client, $"autopelago_state_{team}_{slot}"), cts.Token);
    await game.SetStateStorageAsync(new FileGameStateStorage(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ap-test.json")), cts.Token);

    await game.RunUntilCanceledAsync(cts.Token);
}
catch (OperationCanceledException)
{
}

try
{
    backgroundException?.Throw();
}
catch (OperationCanceledException)
{
}
