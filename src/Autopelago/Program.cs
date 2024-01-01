using System.Collections.Frozen;

using ArchipelagoClientDotNet;

await Helper.ConfigureAwaitFalse();

using ArchipelagoClient client = new(args[0], ushort.Parse(args[1]));

FrozenDictionary<long, string>? locationNamesById = null;
FrozenDictionary<long, string>? itemNamesById = null;
FrozenDictionary<string, long>? locationIdsByName = null;
FrozenDictionary<string, long>? itemIdsByName = null;
HashSet<long>? locationsNotYetSent = null;
HashSet<long>? itemsNotYetReceived = null;

client.DataPackagePacketReceived += OnDataPackagePacketReceived;
ValueTask OnDataPackagePacketReceived(object? sender, DataPackagePacketModel dataPackage, CancellationToken cancellationToken)
{
    if (!dataPackage.Data.Games.TryGetValue(args[2], out GameDataModel? myGame))
    {
        Console.Error.WriteLine("oh no, my game isn't present.");
        Environment.Exit(1);
    }

    locationsNotYetSent = [..myGame.LocationNameToId.Values];
    itemsNotYetReceived = [..myGame.ItemNameToId.Values];

    locationIdsByName = myGame.LocationNameToId.ToFrozenDictionary();
    itemIdsByName = myGame.ItemNameToId.ToFrozenDictionary();

    locationNamesById = myGame.LocationNameToId.Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();
    itemNamesById = myGame.ItemNameToId.Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();

    Console.WriteLine($"Data initialized.  There are {locationNamesById.Count} location(s) and {itemNamesById.Count} item(s).");
    return ValueTask.CompletedTask;
}

client.ConnectedPacketReceived += OnConnectedPacketReceived;
ValueTask OnConnectedPacketReceived(object? sender, ConnectedPacketModel connected, CancellationToken cancellationToken)
{
    locationsNotYetSent!.ExceptWith(connected.CheckedLocations);
    return ValueTask.CompletedTask;
}

client.ReceivedItemsPacketReceived += OnReceivedItemsPacketReceived;
ValueTask OnReceivedItemsPacketReceived(object? sender, ReceivedItemsPacketModel receivedItems, CancellationToken cancellationToken)
{
    itemsNotYetReceived!.ExceptWith(receivedItems.Items.Select(i => i.Item));
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
    if (Volatile.Read(ref x) == 0)
    {
        await client.SayAsync(line);
    }
}

return 0;
