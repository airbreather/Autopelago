using System.Collections.Frozen;
using System.Collections.Immutable;
using ArchipelagoClientDotNet;

await Helper.ConfigureAwaitFalse();

using SemaphoreSlim lck = new(1, 1);

using ArchipelagoClient client = new(args[0], ushort.Parse(args[1]));

FrozenDictionary<int, string>? playerNames = null;
FrozenDictionary<long, string>? allLocationNamesById = null;
FrozenDictionary<long, string>? allItemNamesById = null;
FrozenDictionary<string, long>? allLocationIdsByName = null;
FrozenDictionary<string, long>? allItemIdsByName = null;
FrozenDictionary<long, string>? myLocationNamesById = null;
FrozenDictionary<long, string>? myItemNamesById = null;
FrozenDictionary<string, long>? myLocationIdsByName = null;
FrozenDictionary<string, long>? myItemIdsByName = null;
HashSet<long>? myLocationsNotYetSent = null;
HashSet<long>? myItemsNotYetReceived = null;
long[]? allMyLocations = null;
long[]? allMyItems = null;

client.DataPackagePacketReceived += OnDataPackagePacketReceived;
ValueTask OnDataPackagePacketReceived(object? sender, DataPackagePacketModel dataPackage, CancellationToken cancellationToken)
{
    if (!dataPackage.Data.Games.TryGetValue(args[2], out GameDataModel? myGame))
    {
        Console.Error.WriteLine("oh no, my game isn't present.");
        Environment.Exit(1);
    }

    allLocationIdsByName = dataPackage.Data.Games.Values.SelectMany(game => game.LocationNameToId).ToFrozenDictionary();
    allItemIdsByName = dataPackage.Data.Games.Values.SelectMany(game => game.ItemNameToId).ToFrozenDictionary();

    allLocationNamesById = allLocationIdsByName.Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();
    allItemNamesById = allItemIdsByName.Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();

    allMyLocations = [..myGame.LocationNameToId.Values.Order()];
    allMyItems = [..myGame.ItemNameToId.Values.Order()];

    myLocationsNotYetSent = [..allMyLocations];
    myItemsNotYetReceived = [..allMyItems];

    myLocationIdsByName = myGame.LocationNameToId.ToFrozenDictionary();
    myItemIdsByName = myGame.ItemNameToId.ToFrozenDictionary();

    myLocationNamesById = myGame.LocationNameToId.Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();
    myItemNamesById = myGame.ItemNameToId.Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();

    Console.WriteLine($"Data initialized.  There are {myLocationNamesById.Count} location(s) and {myItemNamesById.Count} item(s).");
    return ValueTask.CompletedTask;
}

client.ConnectedPacketReceived += OnConnectedPacketReceived;
ValueTask OnConnectedPacketReceived(object? sender, ConnectedPacketModel connected, CancellationToken cancellationToken)
{
    playerNames = connected.Players.ToFrozenDictionary(p => p.Slot, p => p.Name);
    myLocationsNotYetSent!.ExceptWith(connected.CheckedLocations);
    return ValueTask.CompletedTask;
}

client.ReceivedItemsPacketReceived += OnReceivedItemsPacketReceived;
async ValueTask OnReceivedItemsPacketReceived(object? sender, ReceivedItemsPacketModel receivedItems, CancellationToken cancellationToken)
{
    await Helper.ConfigureAwaitFalse();
    await lck.WaitAsync(cancellationToken);
    try
    {
        myItemsNotYetReceived!.ExceptWith(receivedItems.Items.Select(i => i.Item));
    }
    finally
    {
        lck.Release();
    }
}

client.PrintJSONPacketReceived += OnPrintJSONPacketReceived;
ValueTask OnPrintJSONPacketReceived(object? sender, PrintJSONPacketModel packet, CancellationToken cancellationToken)
{
    foreach (JSONMessagePartModel part in packet.Data)
    {
        Console.Write(part switch
        {
            PlayerIdJSONMessagePartModel playerId => playerNames![int.Parse(playerId.Text)],
            ItemIdJSONMessagePartModel itemId => allItemNamesById![long.Parse(itemId.Text)],
            LocationIdJSONMessagePartModel locationId => allLocationNamesById![long.Parse(locationId.Text)],
            _ => part.Text,
        });
    }

    Console.WriteLine();
    return ValueTask.CompletedTask;
}

client.RoomUpdatePacketReceived += OnRoomUpdatePacketReceived;
async ValueTask OnRoomUpdatePacketReceived(object? sender, RoomUpdatePacketModel roomUpdate, CancellationToken cancellationToken)
{
    if (roomUpdate.CheckedLocations is ImmutableArray<long> checkedLocations)
    {
        await lck.WaitAsync(cancellationToken);
        try
        {
            myLocationsNotYetSent!.ExceptWith(roomUpdate.CheckedLocations);
        }
        finally
        {
            lck.Release();
        }
    }
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

int nextMod = 0;
Random r = new();
while (true)
{
    await Task.Delay(5000);
    long? location = null;
    int currRoll = r.Next(1, 21), currDC, foundItemCount, sentLocationsCount, accessibleLocationsCount = 0, accessibleUnsentLocationsCount = 0;
    await lck.WaitAsync();
    try
    {
        foundItemCount = allMyItems!.Length - myItemsNotYetReceived!.Count;
        sentLocationsCount = allMyLocations!.Length - myLocationsNotYetSent!.Count;
        currDC = FractionToDC(foundItemCount, allMyItems.Length);
        if (currRoll + (nextMod / 10) >= currDC)
        {
            ArraySegment<long> accessibleLocations = AccessibleLocations();
            accessibleLocationsCount = accessibleLocations.Count;
            long[] accessibleUnsentLocations = accessibleLocations.Where(myLocationsNotYetSent!.Contains).ToArray();
            accessibleUnsentLocationsCount = accessibleUnsentLocations.Length;
            location = accessibleUnsentLocations.Length == 0
                ? -1
                : accessibleUnsentLocations[r.Next(accessibleUnsentLocations.Length)];
        }
    }
    finally
    {
        lck.Release();
    }

    switch (location)
    {
        case null:
            await client.SayAsync($"FAIL.  DC: {currDC} (based on {foundItemCount} of {allMyItems.Length} item(s) found).  Roll: 1d20 ({currRoll}) + {nextMod / 10}.");
            ++nextMod;
            break;

        case -1:
            await client.SayAsync($"PASS.  DC: {currDC} (based on {foundItemCount} of {allMyItems.Length} item(s) found).  Roll: 1d20 ({currRoll}) + {nextMod / 10}.");
            await client.SayAsync($"{accessibleUnsentLocationsCount} of the {accessibleLocationsCount} accessible location(s) (based on {foundItemCount} of {allMyItems.Length} item(s) found) may be sent.");
            ++nextMod;
            break;

        case long val:
            await client.SayAsync($"PASS.  DC: {currDC} (based on {foundItemCount} of {allMyItems.Length} item(s) found).  Roll: 1d20 ({currRoll}) + {nextMod / 10}.");
            await client.SayAsync($"{accessibleUnsentLocationsCount} of the {accessibleLocationsCount} accessible location(s) (based on {foundItemCount} of {allMyItems.Length} item(s) found) may be sent.  Sending one now...");
            nextMod = 0;
            await client.LocationChecksAsync(new[] { val });
            break;
    }
}

static int FractionToDC(int numerator, int denominator) => (numerator / (double)denominator) switch
{
    < 0.2 => 5,
    < 0.5 => 10,
    < 0.7 => 15,
    < 0.9 => 18,
    _ => 20,
};
ArraySegment<long> AccessibleLocations() => new(allMyLocations!, 0, (allMyItems!.Length - myItemsNotYetReceived!.Count) switch
{
    < 4 => 15,
    < 10 => 30,
    < 20 => 50,
    < 40 => 100,
    _ => 200,
});
