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

Console.CancelKeyPress += async (sender, args) =>
{
    args.Cancel = true;
    await cts.CancelAsync();
};

await using WebSocketPacketChannel channel = new(settings.Server, settings.Port);
await channel.ConnectAsync(cts.Token);
ArchipelagoClient client = new(channel);

Game game = new(client, TimeProvider.System);
await game.StartHandshakeAsync(new() { Games = [settings.GameName] }, cts.Token);
await game.FinishHandshakeAsync(new()
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

await game.RunUntilCanceled(cts.Token);
