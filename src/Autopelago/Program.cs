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
for (int i = 0, cnt = settings.Slots.Count; i < cnt; i++)
{
    bool primary = i == 0;
    AutopelagoPlayerSettingsModel slot = settings.Slots[i];
    Unsafe.SkipInit(out int seed);
    Random.Shared.NextBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref seed, 1)));
    ArchipelagoGameRunner runner = new(
        primary: primary,
        minStepInterval: TimeSpan.FromSeconds(slot.OverriddenSettings?.MinSecondsPerGameStep ?? settings.DefaultSettings.MinSecondsPerGameStep),
        maxStepInterval: TimeSpan.FromSeconds(slot.OverriddenSettings?.MaxSecondsPerGameStep ?? settings.DefaultSettings.MaxSecondsPerGameStep),
        player: new(),
        difficultySettings: new(),
        seed: seed,
        server: settings.Server,
        port: settings.Port,
        gameName: settings.GameName,
        slot: slot.Name,
        password: slot.Password
    );
    runners.Add(runner);

    if (primary)
    {
        _ = Task.Run(async () =>
        {
            await Helper.ConfigureAwaitFalse();
            while (Console.ReadLine() is string line)
            {
                await runner.SayAsync(line, cts.Token);
            }
        }, cts.Token);
    }
}

Console.CancelKeyPress += async (sender, args) =>
{
    args.Cancel = true;
    await cts.CancelAsync();
};

return (await Task.WhenAll(runners.Select(r => r.TryRunGameAsync(cts.Token)))).All(x => x)
    ? 0
    : 1;
