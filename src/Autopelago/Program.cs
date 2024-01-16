using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

List<ArchipelagoGameRunner> runners = [];
for (int i = 0, cnt = settings.Slots.Count; i < cnt; i++)
{
    bool primary = i == 0;
    AutopelagoPlayerSettingsModel slot = settings.Slots[i];
    Unsafe.SkipInit(out int seed);
    Random.Shared.NextBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref seed, 1)));
    ArchipelagoGameRunner runner = new(
        minStepInterval: TimeSpan.FromSeconds(slot.OverriddenSettings?.MinSecondsPerGameStep ?? settings.DefaultSettings.MinSecondsPerGameStep),
        maxStepInterval: TimeSpan.FromSeconds(slot.OverriddenSettings?.MaxSecondsPerGameStep ?? settings.DefaultSettings.MaxSecondsPerGameStep),
        player: new(),
        difficultySettings: new(),
        seed: seed,
        server: settings.Server,
        port: settings.Port,
        gameName: settings.GameName,
        slotName: slot.Name,
        password: slot.Password
    );
    runners.Add(runner);

    if (primary)
    {
        _ = Task.Run(async () =>
        {
            await Helper.ConfigureAwaitFalse();

            using ArchipelagoClient textClient = new(settings.Server, settings.Port);
            FrozenDictionary<long, string>? _allLocationNamesById = null;
            FrozenDictionary<long, string>? _allItemNamesById = null;
            FrozenDictionary<int, (int Team, string Name)>? _playerNamesBySlot = null;

            textClient.DataPackagePacketReceived += OnDataPackagePacketReceived;
            textClient.ConnectedPacketReceived += OnConnectedPacketReceived;
            textClient.PrintJSONPacketReceived += OnPrintJSONPacketReceived;
            if (await textClient.TryConnectAsync(settings.GameName, slot.Name, slot.Password, ["AP", "TextOnly"], cts.Token))
            {
                while (Console.ReadLine() is string line)
                {
                    await runner.SayAsync(line, cts.Token);
                }
            }

            ValueTask OnDataPackagePacketReceived(object? sender, DataPackagePacketModel dataPackage, CancellationToken cancellationToken)
            {
                _allLocationNamesById = dataPackage.Data.Games.Values.SelectMany(game => game.LocationNameToId).Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();
                _allItemNamesById = dataPackage.Data.Games.Values.SelectMany(game => game.ItemNameToId).Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();
                return ValueTask.CompletedTask;
            }

            ValueTask OnConnectedPacketReceived(object? sender, ConnectedPacketModel connected, CancellationToken cancellationToken)
            {
                _playerNamesBySlot = connected.Players.ToFrozenDictionary(p => p.Slot, p => (p.Team, p.Name));
                return ValueTask.CompletedTask;
            }

            ValueTask OnPrintJSONPacketReceived(object? sender, PrintJSONPacketModel packet, CancellationToken cancellationToken)
            {
                Console.Write($"[{DateTime.Now:G}] -> ");
                foreach (JSONMessagePartModel part in packet.Data)
                {
                    Console.Write(part switch
                    {
                        PlayerIdJSONMessagePartModel playerId => _playerNamesBySlot![int.Parse(playerId.Text)].Name,
                        ItemIdJSONMessagePartModel itemId => _allItemNamesById![long.Parse(itemId.Text)],
                        LocationIdJSONMessagePartModel locationId => _allLocationNamesById![long.Parse(locationId.Text)],
                        _ => part.Text,
                    });
                }

                Console.WriteLine();
                return ValueTask.CompletedTask;
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
