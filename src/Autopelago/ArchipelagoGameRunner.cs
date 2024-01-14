using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;

using ArchipelagoClientDotNet;

public sealed class ArchipelagoGameRunner : IDisposable
{
    private static readonly Dictionary<string, ItemType> s_progressiveItemTypeByName = new()
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

    private readonly SemaphoreSlim _gameLock = new(1, 1);

    private readonly TimeSpan _minStepInterval;

    private readonly TimeSpan _maxStepInterval;

    private readonly Player _player;

    private readonly GameDifficultySettings _difficultySettings;

    private readonly int _seed;

    private readonly Game _game;

    private readonly ArchipelagoClient _client;

    private readonly string _gameName;

    private readonly string _slot;

    private readonly string? _password;

    private FrozenDictionary<int, string>? _playerNamesBySlot;

    private FrozenDictionary<string, int>? _playerSlotsByName;

    private FrozenDictionary<long, string>? _allLocationNamesById;

    private FrozenDictionary<long, string>? _allItemNamesById;

    private FrozenDictionary<string, long>? _allLocationIdsByName;

    private FrozenDictionary<string, long>? _allItemIdsByName;

    private FrozenDictionary<long, string>? _myLocationNamesById;

    private FrozenDictionary<long, string>? _myItemNamesById;

    private FrozenDictionary<string, long>? _myLocationIdsByName;

    private FrozenDictionary<string, long>? _myItemIdsByName;

    private HashSet<long>? _myLocationsNotYetSent;

    private HashSet<long>? _myItemsNotYetReceived;

    private long[]? _allMyLocations;

    private long[]? _allMyItems;

    public ArchipelagoGameRunner(bool primary, TimeSpan minStepInterval, TimeSpan maxStepInterval, Player player, GameDifficultySettings difficultySettings, int seed, string server, ushort port, string gameName, string slot, string? password)
    {
        _minStepInterval = minStepInterval;
        _maxStepInterval = maxStepInterval;
        _player = player;
        _difficultySettings = difficultySettings;
        _seed = seed;
        _game = new(_difficultySettings, _seed);
        _client = new(server, port);
        _gameName = gameName;
        _slot = slot;
        _password = password;

        // if this is true, we'll just find gaps inside the existing item ranges and exit.
        if (false)
        {
            _client.NeedDataPackageForAllGames = true;
            _client.DataPackagePacketReceived += FindIdRangeGapsAndExit;
        }

        _client.DataPackagePacketReceived += OnDataPackagePacketReceived;
        _client.ConnectedPacketReceived += OnConnectedPacketReceived;
        _client.ReceivedItemsPacketReceived += OnReceivedItemsPacketReceived;
        if (primary)
        {
            _client.PrintJSONPacketReceived += OnPrintJSONPacketReceived;
        }

        _client.RoomUpdatePacketReceived += OnRoomUpdatePacketReceived;
    }

    public async Task<bool> TryRunGameAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        if (!await _client.TryConnectAsync(_gameName, _slot, _password, cancellationToken))
        {
            return false;
        }

        long averageSteps = await CalculateAverageStepsAsync(_seed, _difficultySettings, _player, cancellationToken);
        TimeSpan medianStepInterval = (_minStepInterval + _maxStepInterval) / 2;
        await _client.SayAsync($"With my current settings, a non-randomized playthrough would take {(medianStepInterval * averageSteps).FormatMyWay()} to complete.", cancellationToken);

        _game.CompletedLocationCheck += OnCompletedLocationCheckAsync;
        _game.FailedLocationCheck += OnFailedLocationCheckAsync;
        _game.MovingToRegion += OnMovingToRegionAsync;
        _game.MovedToRegion += OnMovedToRegionAsync;

        await _client.StatusUpdateAsync(ArchipelagoClientStatus.Playing, cancellationToken);
        Task nextDelay = Task.Delay(NextStepInterval(), cancellationToken);
        long? reportedBlockedTime = null;
        long lastUpdateTime = Stopwatch.GetTimestamp();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!await OneStepAsync())
                {
                    break;
                }
            }

            await _client.SayAsync("I've completed my goal!  Wrapping up now...", cancellationToken);
            await _client.StatusUpdateAsync(ArchipelagoClientStatus.Goal, cancellationToken);
            await _client.StopAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
        }

        await _client.StopAsync(CancellationToken.None);
        return false;

        async ValueTask<bool> OneStepAsync()
        {
            await Helper.ConfigureAwaitFalse();

            await nextDelay;
            nextDelay = Task.Delay(NextStepInterval(), cancellationToken);
            bool step;
            await _gameLock.WaitAsync(cancellationToken);
            try
            {
                step = await _game.StepAsync(_player, cancellationToken);
            }
            finally
            {
                _gameLock.Release();
            }

            if (_game.IsCompleted)
            {
                return false;
            }

            if (step)
            {
                reportedBlockedTime = null;
            }
            else if (reportedBlockedTime is not { } ts || Stopwatch.GetElapsedTime(ts) > TimeSpan.FromMinutes(5))
            {
                await _client.SayAsync("I have nothing to do right now...", cancellationToken);
                reportedBlockedTime = Stopwatch.GetTimestamp();
            }

            if (Stopwatch.GetElapsedTime(lastUpdateTime) > TimeSpan.FromMinutes(2))
            {
                await _client.StatusUpdateAsync(ArchipelagoClientStatus.Playing, cancellationToken);
                lastUpdateTime = Stopwatch.GetTimestamp();
            }

            return true;
        }
    }

    public async ValueTask SayAsync(string line, CancellationToken cancellationToken)
    {
        await _client.SayAsync(line, cancellationToken);
    }

    public void Dispose()
    {
        _client.Dispose();
        _gameLock.Dispose();
    }

    private ValueTask OnDataPackagePacketReceived(object? sender, DataPackagePacketModel dataPackage, CancellationToken cancellationToken)
    {
        if (!dataPackage.Data.Games.TryGetValue(_gameName, out GameDataModel? myGame))
        {
            Console.Error.WriteLine("oh no, my game isn't present.");
            Environment.Exit(1);
        }

        _allLocationIdsByName = dataPackage.Data.Games.Values.SelectMany(game => game.LocationNameToId).ToFrozenDictionary();
        _allItemIdsByName = dataPackage.Data.Games.Values.SelectMany(game => game.ItemNameToId).ToFrozenDictionary();

        _allLocationNamesById = _allLocationIdsByName.Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();
        _allItemNamesById = _allItemIdsByName.Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();

        _allMyLocations = [..myGame.LocationNameToId.Values.Order()];
        _allMyItems = [..myGame.ItemNameToId.Values.Order()];

        _myLocationsNotYetSent = [.._allMyLocations];
        _myItemsNotYetReceived = [.._allMyItems];

        _myLocationIdsByName = myGame.LocationNameToId.ToFrozenDictionary();
        _myItemIdsByName = myGame.ItemNameToId.ToFrozenDictionary();

        _myLocationNamesById = myGame.LocationNameToId.Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();
        _myItemNamesById = myGame.ItemNameToId.Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();

        return ValueTask.CompletedTask;
    }

    private ValueTask OnConnectedPacketReceived(object? sender, ConnectedPacketModel connected, CancellationToken cancellationToken)
    {
        _playerNamesBySlot = connected.Players.ToFrozenDictionary(p => p.Slot, p => p.Name);
        _playerSlotsByName = connected.Players.ToFrozenDictionary(p => p.Name, p => p.Slot);
        foreach (long locationId in connected.CheckedLocations)
        {
            _game.MarkLocationChecked(locationId);
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask OnReceivedItemsPacketReceived(object? sender, ReceivedItemsPacketModel receivedItems, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        await _gameLock.WaitAsync(cancellationToken);
        try
        {
            foreach (ItemModel item in receivedItems.Items)
            {
                string name = _myItemNamesById![item.Item];
                bool isAdvancement = item.Flags.HasFlag(ArchipelagoItemFlags.LogicalAdvancement);
                _game.ReceiveItem(item.Item, isAdvancement ? s_progressiveItemTypeByName[name] : ItemType.Filler);
            }
        }
        finally
        {
            _gameLock.Release();
        }
    }

    private ValueTask OnPrintJSONPacketReceived(object? sender, PrintJSONPacketModel packet, CancellationToken cancellationToken)
    {
        foreach (JSONMessagePartModel part in packet.Data)
        {
            Console.Write(part switch
            {
                PlayerIdJSONMessagePartModel playerId => _playerNamesBySlot![int.Parse(playerId.Text)],
                ItemIdJSONMessagePartModel itemId => _allItemNamesById![long.Parse(itemId.Text)],
                LocationIdJSONMessagePartModel locationId => _allLocationNamesById![long.Parse(locationId.Text)],
                _ => part.Text,
            });
        }

        Console.WriteLine();
        return ValueTask.CompletedTask;
    }

    private async ValueTask OnRoomUpdatePacketReceived(object? sender, RoomUpdatePacketModel roomUpdate, CancellationToken cancellationToken)
    {
        if (roomUpdate.CheckedLocations is ImmutableArray<long> checkedLocations)
        {
            await _gameLock.WaitAsync(cancellationToken);
            try
            {
                foreach (long locationId in checkedLocations)
                {
                    _game.MarkLocationChecked(locationId);
                }
            }
            finally
            {
                _gameLock.Release();
            }
        }
    }

    private async ValueTask OnCompletedLocationCheckAsync(object? sender, CompletedLocationCheckEventArgs args, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        await _client.LocationChecksAsync(new[] { args.Location }, cancellationToken);
    }

    private async ValueTask OnFailedLocationCheckAsync(object? sender, FailedLocationCheckEventArgs args, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        await _client.SayAsync($"failed a check ({_myLocationNamesById![args.Location]})", cancellationToken);
    }

    private async ValueTask OnGameReset(object? sender, ResetGameEventArgs args, CancellationToken cancellationToken)
    {
        switch (args.Reasons)
        {
            case ResetReasons.FasterTravelTime:
                await _client.SayAsync("Finished a reset for better travel time.", cancellationToken);
                break;

            default:
                await _client.SayAsync($"Finished resetting the game for a combination of reasons that Joe hasn't written a special message for yet: {args.Reasons}.", cancellationToken);
                break;
        }
    }

    private async ValueTask OnMovingToRegionAsync(object? sender, MovingToRegionEventArgs args, CancellationToken cancellationToken)
    {
        if (args.TotalTravelSteps == 0)
        {
            // we'll immediately do a check, no need to communicate extra.
            return;
        }

        await Helper.ConfigureAwaitFalse();

        int actualTravelSteps = (args.RemainingTravelSteps + _player.MovementSpeed - 1) / _player.MovementSpeed;
        TimeSpan medianStepInterval = (_maxStepInterval + _minStepInterval) / 2;
        if (args.ResettingFirst)
        {
            await _client.SayAsync($"Moving to {args.TargetRegion} (remaining: ~{(actualTravelSteps * medianStepInterval).FormatMyWay()} after game reset)", cancellationToken);
        }
        else
        {
            await _client.SayAsync($"Moving to {args.TargetRegion} (remaining: ~{(actualTravelSteps * medianStepInterval).FormatMyWay()})", cancellationToken);
        }
    }

    private async ValueTask OnMovedToRegionAsync(object? sender, MovedToRegionEventArgs args, CancellationToken cancellationToken)
    {
        if (args.TotalTravelSteps == 0)
        {
            // we'll immediately do a check, no need to communicate extra.
            return;
        }

        await Helper.ConfigureAwaitFalse();
        await _client.SayAsync($"Arrived at {args.TargetRegion}.", cancellationToken);
    }

    private async ValueTask<long> CalculateAverageStepsAsync(int seed, GameDifficultySettings difficultySettings, Player player, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        long simulatedTotalStepCount = 0;
        long locationGoal = Game.s_locationsByRegion[Region.TryingForGoal].Single();

        const int SimulationCount = 100_000;
        await Parallel.ForAsync(0, SimulationCount, cancellationToken, async (i, cancellationToken) =>
        {
            await Helper.ConfigureAwaitFalse();
            Game simulatedGame = new(difficultySettings, seed + i);
            long? itemToSendBeforeNextStep = null;
            simulatedGame.CompletedLocationCheck += SimulatedOnCompletedLocationCheckAsync;
            ValueTask SimulatedOnCompletedLocationCheckAsync(object? sender, CompletedLocationCheckEventArgs args, CancellationToken cancellationToken)
            {
                itemToSendBeforeNextStep = args.Location;
                return ValueTask.CompletedTask;
            }

            long currentGameStepCount = 0;
            while (!simulatedGame.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await simulatedGame.StepAsync(player, cancellationToken);
                ++currentGameStepCount;
                if (itemToSendBeforeNextStep is { } location)
                {
                    itemToSendBeforeNextStep = null;
                    string name = _myItemNamesById![location];
                    simulatedGame.ReceiveItem(location, s_progressiveItemTypeByName.TryGetValue(name, out ItemType progressiveItemType) ? progressiveItemType : ItemType.Filler);
                }
            }

            Interlocked.Add(ref simulatedTotalStepCount, currentGameStepCount);
        });

        return simulatedTotalStepCount / SimulationCount;
    }

    private ValueTask FindIdRangeGapsAndExit(object? sender, DataPackagePacketModel dataPackage, CancellationToken cancellationToken)
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

    private TimeSpan NextStepInterval()
    {
        long tickRange = _maxStepInterval.Ticks - _minStepInterval.Ticks;
        long extraTicks = (long)(Random.Shared.NextDouble() * tickRange);
        return _minStepInterval + TimeSpan.FromTicks(extraTicks);
    }
}
