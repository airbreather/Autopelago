using ArchipelagoClientDotNet;

await Helper.ConfigureAwaitFalse();

using ArchipelagoClient client = new(args[0], ushort.Parse(args[1]));
client.DataPackagePacketReceived += OnDataPackagePacketReceived;

ValueTask OnDataPackagePacketReceived(object? sender, DataPackagePacketModel dataPackage, CancellationToken cancellationToken)
{
    foreach ((string game, GameDataModel gameData) in dataPackage.Data.Games)
    {
        Console.WriteLine($"data for {game}:");
        Console.WriteLine($"checksum: {gameData.Checksum}");
        Console.WriteLine($"some of its items: {string.Join(Environment.NewLine, gameData.ItemNameToId.OrderBy(x => x.Value).Take(15).Select(kvp => $"    {kvp.Key} -> {kvp.Value}").Prepend(""))}");
        Console.WriteLine($"some of its locations: {string.Join(Environment.NewLine, gameData.LocationNameToId.OrderBy(x => x.Value).Take(15).Select(kvp => $"    {kvp.Key} -> {kvp.Value}").Prepend(""))}");
        Console.WriteLine();
    }

    return ValueTask.CompletedTask;
}

client.PrintJSONPacketReceived += OnPrintJSONPacketReceived;
ValueTask OnPrintJSONPacketReceived(object? sender, PrintJSONPacketModel packet, CancellationToken cancellationToken)
{
    foreach (JSONMessagePartModel part in packet.Data)
    {
        Console.Write(part.Text);
    }

    Console.WriteLine();
    return ValueTask.CompletedTask;
}

if (!await client.TryConnectAsync(args[2], args[3], args.ElementAtOrDefault(4)))
{
    Console.Error.WriteLine("oh no, we failed to connect.");
    return 1;
}

int x = 0;
Console.CancelKeyPress += async (sender, args) =>
{
    args.Cancel = true;
    if (Interlocked.CompareExchange(ref x, 1, 0) != 0)
    {
        return;
    }

    await client.StopAsync();
    Environment.Exit(0);
};

while (Console.ReadLine() is string line)
{
    if (Volatile.Read(ref x) != 0)
    {
        continue;
    }

    await client.SendAsync(new[] { new SayPacketModel
    {
        Text = line,
    }});
}

return 0;
