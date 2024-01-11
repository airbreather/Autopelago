using ArchipelagoClientDotNet;

await Helper.ConfigureAwaitFalse();

CancellationTokenSource cts = new();

ArchipelagoGameRunner runner = new(
    stepInterval: TimeSpan.FromSeconds(double.Parse(args[0])),
    server: args[1],
    port: ushort.Parse(args[2]),
    gameName: args[3],
    slot: args[4],
    password: args.ElementAtOrDefault(5)
);

Console.CancelKeyPress += async (sender, args) =>
{
    args.Cancel = true;
    await cts.CancelAsync();
};

_ = Task.Run(async () =>
{
    await Helper.ConfigureAwaitFalse();
    while (Console.ReadLine() is string line)
    {
        await runner.SayAsync(line, cts.Token);
    }
});

return await runner.TryRunGameAsync(cts.Token)
    ? 0
    : 1;
