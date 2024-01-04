using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

int numMyProgressionItemsNotYetReceived = 40;
HashSet<long> myProgressionItemsReceived = [];

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
        foreach (ItemModel item in receivedItems.Items)
        {
            myItemsNotYetReceived!.Remove(item.Item);
            if (item.Flags.HasFlag(ArchipelagoItemFlags.LogicalAdvancement) && myProgressionItemsReceived.Add(item.Item))
            {
                --numMyProgressionItemsNotYetReceived;
            }
        }
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

_ = Task.Run(async () =>
{
    await Helper.ConfigureAwaitFalse();
    while (Console.ReadLine() is string line)
    {
        await client.SayAsync(line);
    }
});

TimeSpan stepInterval = TimeSpan.FromSeconds(30);
const int Simulations = 1_000_000;
long totalSteps = 0;
Unsafe.SkipInit(out int seedSeed);
Random.Shared.NextBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref seedSeed, 1)));
Player player = new();
GameDifficultySettings difficultySettings = new();
await Parallel.ForAsync(0, Simulations, async (i, cancellationToken) =>
{
    await Helper.ConfigureAwaitFalse();

    Game game = new(difficultySettings, i);
    game.CompletedLocationCheck += OnCompletedLocationCheckAsync;

    bool done = false;
    long steps = 0;
    while (!done)
    {
        ++steps;
        await game.StepAsync(player, cancellationToken);
    }

    Interlocked.Add(ref totalSteps, steps);
    async ValueTask OnCompletedLocationCheckAsync(object? sender, long locationId, CancellationToken cancellationToken)
    {
        Region region = Game.s_regionByLocation[locationId];
        if (region == Region.TryingForGoal)
        {
            done = true;
            return;
        }

        await game.ReceiveItemAsync(region switch
        {
            Region.A => ItemType.A,
            Region.B => ItemType.B,
            Region.C => ItemType.C,
            Region.D => ItemType.D,
            Region.E => ItemType.E,
            Region.F => ItemType.F,
            _ => ItemType.Normal,
        }, cancellationToken);
    }
});
await client.SayAsync($"Given the current settings, if I never got blocked, then I would be able to complete this game in approximately {(stepInterval * (totalSteps / (double)Simulations)).TotalMinutes:N2} minute(s) (seed: {seedSeed}).");

await client.StatusUpdateAsync(ArchipelagoClientStatus.Playing);

int nextMod = 0;
int reportedBlocked = 0;
bool completedGoal = false;
Task nextDelay = Task.Delay(stepInterval);
while (true)
{
    await nextDelay;
    nextDelay = Task.Delay(stepInterval);
    long? location = null;
    bool attemptGoalCompletion;
    int currRoll = -1, currDC, sentLocationsCount, accessibleLocationsCount, accessibleUnsentLocationsCount;
    await lck.WaitAsync();
    try
    {
        attemptGoalCompletion = !completedGoal && myProgressionItemsReceived.Count == 40;
        sentLocationsCount = allMyLocations!.Length - myLocationsNotYetSent!.Count;
        currDC = myProgressionItemsReceived.Count switch
        {
            < 4 => 5,
            < 10 => 10,
            < 20 => 12,
            < 40 => 15,
            _ => 20,
        };
        ArraySegment<long> accessibleLocations = new(allMyLocations!, 0, myProgressionItemsReceived.Count switch
        {
            < 4 => 15,
            < 10 => 30,
            < 20 => 50,
            < 40 => 100,
            _ => 200,
        });
        accessibleLocationsCount = accessibleLocations.Count;
        long[] accessibleUnsentLocations = accessibleLocations.Where(myLocationsNotYetSent!.Contains).ToArray();
        accessibleUnsentLocationsCount = accessibleUnsentLocations.Length;
        if (accessibleUnsentLocationsCount > 0 && (currRoll = Random.Shared.Next(1, 21)) + (nextMod / 10) >= currDC)
        {
            location = accessibleUnsentLocations.Length < 1
                ? -1
                : accessibleUnsentLocations[Random.Shared.Next(accessibleUnsentLocations.Length)];
        }
    }
    finally
    {
        lck.Release();
    }

    if (attemptGoalCompletion)
    {
        if (currRoll + (nextMod / 10) < currDC)
        {
            await client.SayAsync($"{currRoll}+{nextMod / 10} < {currDC} ({myProgressionItemsReceived.Count}/40)");
            ++nextMod;
        }
        else
        {
            await client.SayAsync($"{currRoll}+{nextMod / 10} >= {currDC} ({myProgressionItemsReceived.Count}/40)");
            await client.SayAsync("I've completed my goal!  Wrapping up now...");
            await client.StatusUpdateAsync(ArchipelagoClientStatus.Goal);
            completedGoal = true;
        }

        continue;
    }

    if (accessibleUnsentLocationsCount < 1)
    {
        if (reportedBlocked % 10 == 0)
        {
            await client.SayAsync($"0/{accessibleLocationsCount} accessible checks ({myProgressionItemsReceived.Count}/40)");
        }

        ++reportedBlocked;
        continue;
    }

    reportedBlocked = 0;
    switch (location)
    {
        case null:
            await client.SayAsync($"{currRoll}+{nextMod / 10} < {currDC} ({myProgressionItemsReceived.Count}/40)");
            ++nextMod;
            break;

        case long val:
            await client.SayAsync($"{currRoll}+{nextMod / 10} >= {currDC} ({myProgressionItemsReceived.Count}/40) -> {accessibleUnsentLocationsCount}/{accessibleLocationsCount} accessible");
            nextMod = 0;
            await client.LocationChecksAsync(new[] { val });
            break;
    }
}
