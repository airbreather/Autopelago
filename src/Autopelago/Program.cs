using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using ArchipelagoClientDotNet;

await Helper.ConfigureAwaitFalse();

using SemaphoreSlim gameLock = new(1, 1);
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

Dictionary<string, ItemType> progressiveItemTypeByName = new()
{
    ["Set of Three Seashells"] = ItemType.Rat,
    ["Chewed Bar of Soap"] = ItemType.Rat,
    ["Refreshing Glass of Lemonade"] = ItemType.Rat,
    ["Empty Snail Shell"] = ItemType.Rat,
    ["Bag of Powdered Sugar"] = ItemType.Rat,
    ["Half of a Worm"] = ItemType.Rat,
    ["Loose Screw"] = ItemType.Rat,
    ["Organic Apple Core"] = ItemType.Rat,
    ["AAAAAA battery"] = ItemType.Rat,
    ["Faux Dalmation-Skin Coat"] = ItemType.Rat,
    ["Off-brand Soda Can"] = ItemType.Rat,
    ["Generic Green Slime"] = ItemType.Rat,
    ["Handful of Loose Marbles"] = ItemType.Rat,
    ["Discarded Video Game Cartridge"] = ItemType.Rat,
    ["Packet of Ketchup"] = ItemType.Rat,
    ["An Entire Roast Chicken"] = ItemType.Rat,
    ["Actual Lava Lamp"] = ItemType.Rat,
    ["Soup with a Hair in it"] = ItemType.Rat,
    ["Proof that Aliens Exist"] = ItemType.Rat,
    ["Small chain of Islands"] = ItemType.Rat,
    ["Pluto"] = ItemType.Rat,
    ["Too Many Crabs"] = ItemType.Rat,
    ["Oxford Comma"] = ItemType.Rat,
    ["Printer Driver Disc"] = ItemType.Rat,
    ["Ticket for the Off-broadway Musical Rats"] = ItemType.Rat,
    ["Can of Spam"] = ItemType.Rat,
    ["Human-sized Skateboard"] = ItemType.Rat,
    ["Holographic Draw Four Card"] = ItemType.Rat,
    ["Loose Staples"] = ItemType.Rat,
    ["Beanie Baby in a Pot of Chili"] = ItemType.Rat,
    ["Red Cape"] = ItemType.Rat,
    ["Radio Controlled Car"] = ItemType.Rat,
    ["Lovely Bunch of Coconuts"] = ItemType.Rat,
    ["Dihydrogen Monoxide"] = ItemType.Rat,
    ["Taco Salad that is Only Tacos"] = ItemType.A,
    ["Blue Turtle Shell"] = ItemType.B,
    ["Statue of David's Dog"] = ItemType.C,
    ["Yesterday's Horoscope"] = ItemType.D,
    ["Aggressive Post-it Notes"] = ItemType.E,
    ["Cheesnado"] = ItemType.F,
    ["Lockheed SR-71 Blackbird"] = ItemType.Goal,
};

// if this is true, we'll just find gaps inside the existing item ranges and exit.
if (false)
{
    client.NeedDataPackageForAllGames = true;
    client.DataPackagePacketReceived += FindIdRangeGapsAndExit;
}

TimeSpan stepInterval = TimeSpan.FromSeconds(30);
Unsafe.SkipInit(out int seed);
Random.Shared.NextBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref seed, 1)));
Player player = new();
GameDifficultySettings difficultySettings = new();
Game game = new(difficultySettings, seed);

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
    foreach (long locationId in connected.CheckedLocations)
    {
        game.MarkLocationChecked(locationId);
    }

    return ValueTask.CompletedTask;
}

client.ReceivedItemsPacketReceived += OnReceivedItemsPacketReceived;
async ValueTask OnReceivedItemsPacketReceived(object? sender, ReceivedItemsPacketModel receivedItems, CancellationToken cancellationToken)
{
    await Helper.ConfigureAwaitFalse();
    await gameLock.WaitAsync(cancellationToken);
    try
    {
        foreach (ItemModel item in receivedItems.Items)
        {
            string name = myItemNamesById![item.Item];
            bool isAdvancement = item.Flags.HasFlag(ArchipelagoItemFlags.LogicalAdvancement);
            game.ReceiveItem(item.Item, isAdvancement ? progressiveItemTypeByName[name] : ItemType.Filler);
        }
    }
    finally
    {
        gameLock.Release();
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
        await gameLock.WaitAsync(cancellationToken);
        try
        {
            foreach (long locationId in checkedLocations)
            {
                game.MarkLocationChecked(locationId);
            }
        }
        finally
        {
            gameLock.Release();
        }
    }
}

if (!await client.TryConnectAsync(args[2], args[3], args.ElementAtOrDefault(4)))
{
    Console.Error.WriteLine("oh no, we failed to connect.");
    return 1;
}

long averageSteps = await CalculateAverageStepsAsync(seed, difficultySettings, player);
await client.SayAsync($"With my current settings, a non-randomized playthrough would take {(stepInterval * averageSteps).TotalMinutes} minutes to complete.");

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

game.CompletedLocationCheck += OnCompletedLocationCheckAsync;
async ValueTask OnCompletedLocationCheckAsync(object? sender, long location, CancellationToken cancellationToken)
{
    await Helper.ConfigureAwaitFalse();
    await client.LocationChecksAsync(new[] { location }, cancellationToken);
}

game.MovingToRegion += OnMovingToRegionAsync;
async ValueTask OnMovingToRegionAsync(object? sender, (Region From, Region To) args, CancellationToken cancellationToken)
{
    await Helper.ConfigureAwaitFalse();
    await client.SayAsync($"Moving to {args.To}...", cancellationToken);
}

game.MovedToRegion += OnMovedToRegionAsync;
async ValueTask OnMovedToRegionAsync(object? sender, Region region, CancellationToken cancellationToken)
{
    await Helper.ConfigureAwaitFalse();
    await client.SayAsync($"Arrived at {region}.", cancellationToken);
}

await client.StatusUpdateAsync(ArchipelagoClientStatus.Playing);

Task nextDelay = Task.Delay(stepInterval);
long? reportedBlockedTime = null;
while (true)
{
    await nextDelay;
    nextDelay = Task.Delay(stepInterval);
    bool step;
    await gameLock.WaitAsync();
    try
    {
        step = await game.StepAsync(player);
    }
    finally
    {
        gameLock.Release();
    }

    if (game.IsCompleted)
    {
        break;
    }

    if (step)
    {
        reportedBlockedTime = null;
    }
    else
    {
        if (reportedBlockedTime is not { } ts || Stopwatch.GetElapsedTime(ts) > TimeSpan.FromMinutes(5))
        {
            await client.SayAsync("I have nothing to do right now...");
            reportedBlockedTime = Stopwatch.GetTimestamp();
        }
    }
}

await client.SayAsync("I've completed my goal!  Wrapping up now...");
await client.StatusUpdateAsync(ArchipelagoClientStatus.Goal);
await client.StopAsync();
return 0;

ValueTask FindIdRangeGapsAndExit(object? sender, DataPackagePacketModel dataPackage, CancellationToken cancellationToken)
{
    Dictionary<long, string> itemToGame = [];
    Dictionary<long, string> locationToGame = [];
    foreach ((string gameName, GameDataModel gameData) in dataPackage.Data.Games)
    {
        foreach (long itemId in gameData.ItemNameToId.Values)
        {
            if (!itemToGame.TryAdd(itemId, gameName))
            {
                Console.WriteLine($"'{gameName}' and '{itemToGame[itemId]}' both use item ID {itemId}");
            }
        }

        foreach (long locationId in gameData.LocationNameToId.Values)
        {
            if (!locationToGame.TryAdd(locationId, gameName))
            {
                Console.WriteLine($"'{gameName}' and '{locationToGame[locationId]}' both use location ID {locationId}");
            }
        }
    }

    ImmutableArray<long> allItems = [..itemToGame.Keys.Where(k => k > 0).Order()];
    for (int i = 1; i < allItems.Length; i++)
    {
        if (allItems[i] - allItems[i - 1] > 10000)
        {
            Console.WriteLine($"found a range of {allItems[i] - allItems[i - 1]} items from {allItems[i - 1]} to {allItems[i]}");
        }
    }

    ImmutableArray<long> allLocations = [..locationToGame.Keys.Where(k => k > 0).Order()];
    for (int i = 1; i < allLocations.Length; i++)
    {
        if (allLocations[i] - allLocations[i - 1] > 10000)
        {
            Console.WriteLine($"found a range of {allLocations[i] - allLocations[i - 1]} locations from {allLocations[i - 1]} to {allLocations[i]}");
        }
    }

    Environment.Exit(0);
    return ValueTask.CompletedTask;
}

async ValueTask<long> CalculateAverageStepsAsync(int seed, GameDifficultySettings difficultySettings, Player player)
{
    await Helper.ConfigureAwaitFalse();
    long simulatedTotalStepCount = 0;
    long locationGoal = Game.s_locationsByRegion[Region.TryingForGoal].Single();

    const int SimulationCount = 100_000;
    await Parallel.ForAsync(0, SimulationCount, async (i, cancellationToken) =>
    {
        await Helper.ConfigureAwaitFalse();
        Game simulatedGame = new(difficultySettings, seed + i);
        long? itemToSendBeforeNextStep = null;
        simulatedGame.CompletedLocationCheck += SimulatedOnCompletedLocationCheckAsync;
        ValueTask SimulatedOnCompletedLocationCheckAsync(object? sender, long location, CancellationToken cancellationToken)
        {
            itemToSendBeforeNextStep = location;
            return ValueTask.CompletedTask;
        }

        long currentGameStepCount = 0;
        while (!simulatedGame.IsCompleted)
        {
            await simulatedGame.StepAsync(player, cancellationToken);
            ++currentGameStepCount;
            if (itemToSendBeforeNextStep is { } location)
            {
                itemToSendBeforeNextStep = null;
                string name = myItemNamesById![location];
                simulatedGame.ReceiveItem(location, progressiveItemTypeByName.TryGetValue(name, out ItemType progressiveItemType) ? progressiveItemType : ItemType.Filler);
            }
        }

        Interlocked.Add(ref simulatedTotalStepCount, currentGameStepCount);
    });

    return simulatedTotalStepCount / SimulationCount;
}
