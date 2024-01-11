using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using ArchipelagoClientDotNet;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

await Helper.ConfigureAwaitFalse();

string settingsYaml = await File.ReadAllTextAsync(args[0]);
AutopelagoSettingsModel settings = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .Build()
    .Deserialize<AutopelagoSettingsModel>(settingsYaml);

CancellationTokenSource cts = new();

List<ArchipelagoGameRunner> runners = [];
foreach (AutopelagoPlayerSettingsModel slot in settings.Slots)
{
    Unsafe.SkipInit(out int seed);
    Random.Shared.NextBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref seed, 1)));
    runners.Add(new(
        stepInterval: TimeSpan.FromSeconds(slot.OverriddenSettings?.SecondsPerGameStep ?? settings.DefaultSettings.SecondsPerGameStep),
        player: new(),
        difficultySettings: new(),
        seed: seed,
        server: settings.Server,
        port: settings.Port,
        gameName: settings.GameName,
        slot: slot.Name,
        password: slot.Password
    ));
}

Console.CancelKeyPress += async (sender, args) =>
{
    args.Cancel = true;
    await cts.CancelAsync();
};

return (await Task.WhenAll(runners.Select(r => r.TryRunGameAsync(cts.Token)))).All(x => x)
    ? 0
    : 1;
