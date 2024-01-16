using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using ArchipelagoClientDotNet;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PersistentState))]
internal sealed partial class SourceGenerationContext : JsonSerializerContext
{
}

public sealed class ArchipelagoGameRunner : IDisposable
{
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        TypeInfoResolver = SourceGenerationContext.Default,
    };

    private static readonly Dictionary<string, ItemType> s_keyItemTypeByName = new()
    {
        ["A Cookie"] = ItemType.A,
        ["Fresh Banana Peel"] = ItemType.B,
        ["MacGuffin"] = ItemType.C,
        ["Blue Turtle Shell"] = ItemType.D,
        ["Red Matador\'s Cape"] = ItemType.E,
        ["Pair of Fake Mouse Ears"] = ItemType.F,
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

    private readonly string _slotName;

    private readonly string? _password;

    private int? _team;

    private int? _slotNumber;

    private FrozenDictionary<long, string>? _myLocationNamesById;

    private FrozenDictionary<long, string>? _myItemNamesById;


    public ArchipelagoGameRunner(TimeSpan minStepInterval, TimeSpan maxStepInterval, Player player, GameDifficultySettings difficultySettings, int seed, string server, ushort port, string gameName, string slotName, string? password)
    {
        _minStepInterval = minStepInterval;
        _maxStepInterval = maxStepInterval;
        _player = player;
        _difficultySettings = difficultySettings;
        _seed = seed;
        _game = new(_difficultySettings, _seed);
        _client = new(server, port);
        _gameName = gameName;
        _slotName = slotName;
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
        _client.RoomUpdatePacketReceived += OnRoomUpdatePacketReceived;
    }

    public async Task<bool> TryRunGameAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        if (!await _client.TryConnectAsync(_gameName, _slotName, _password, ["AP"], cancellationToken))
        {
            return false;
        }

        string stateKey = $"autopelago_state_{_team}_{_slotNumber}";
        RetrievedPacketModel gameStatePacket = await _client.GetAsync([stateKey], cancellationToken);
        if (gameStatePacket.Keys.TryGetValue(stateKey, out JsonElement element))
        {
            if (JsonSerializer.Deserialize<PersistentState>(element, s_jsonSerializerOptions) is PersistentState state)
            {
                _game.InitState(state);
            }
        }

        _game.ResetGame += OnGameResetAsync;
        _game.CompletedLocationCheck += OnCompletedLocationCheckAsync;
        _game.FailedLocationCheck += OnFailedLocationCheckAsync;
        _game.MovingToRegion += OnMovingToRegionAsync;
        _game.MovedToRegion += OnMovedToRegionAsync;
        _game.AurasAdded += OnAurasAddedAsync;
        _game.AurasExpired += OnAurasExpiredAsync;

        await _client.StatusUpdateAsync(ArchipelagoClientStatus.Playing, cancellationToken);
        Task nextDelay = Task.Delay(NextStepInterval(), cancellationToken);
        long? reportedBlockedTime = null;
        long lastUpdateTime = Stopwatch.GetTimestamp();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PersistentState gameState = await OneStepAsync();
                DataStorageOperationModel operation = new()
                {
                    Operation = ArchipelagoDataStorageOperationType.Replace,
                    Value = JsonSerializer.SerializeToNode(gameState, s_jsonSerializerOptions)!,
                };
                await _client.SetAsync(stateKey, [operation], cancellationToken: cancellationToken);
                if (gameState.AchievedGoal)
                {
                    await _client.SayAsync("I've completed my goal!  Wrapping up now...", cancellationToken);
                    await _client.StatusUpdateAsync(ArchipelagoClientStatus.Goal, cancellationToken);
                    await _client.StopAsync(cancellationToken);
                    return true;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        await _client.StopAsync(CancellationToken.None);
        return false;

        async ValueTask<PersistentState> OneStepAsync()
        {
            await Helper.ConfigureAwaitFalse();

            await nextDelay;
            bool step;
            PersistentState gameState;
            await _gameLock.WaitAsync(cancellationToken);
            try
            {
                step = await _game.StepAsync(_player, cancellationToken);
                gameState = _game.State;
            }
            finally
            {
                _gameLock.Release();
            }

            nextDelay = Task.Delay(NextStepInterval() * gameState.StepIntervalMultiplier, cancellationToken);
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

            return gameState;
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

        _myLocationNamesById = myGame.LocationNameToId.Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();
        _myItemNamesById = myGame.ItemNameToId.Select(kvp => KeyValuePair.Create(kvp.Value, kvp.Key)).ToFrozenDictionary();

        return ValueTask.CompletedTask;
    }

    private ValueTask OnConnectedPacketReceived(object? sender, ConnectedPacketModel connected, CancellationToken cancellationToken)
    {
        _team = connected.Team;
        _slotNumber = connected.Slot;
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
                ItemType itemType = Classify(item);
                await _game.ReceiveItem(item.Item, itemType, cancellationToken);
            }
        }
        finally
        {
            _gameLock.Release();
        }
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

    private async ValueTask OnGameResetAsync(object? sender, ResetGameEventArgs args, CancellationToken cancellationToken)
    {
        switch (args.State.ReasonsToReset)
        {
            case ResetReasons.FasterTravelTime:
                await _client.SayAsync("Finished a reset for better travel time.", cancellationToken);
                break;

            default:
                await _client.SayAsync($"Finished resetting the game for a combination of reasons that Joe hasn't written a special message for yet: {args.State.ReasonsToReset}.", cancellationToken);
                break;
        }
    }

    private async ValueTask OnMovingToRegionAsync(object? sender, MovingToRegionEventArgs args, CancellationToken cancellationToken)
    {
        if (args.TotalTravelUnits == 0)
        {
            // we'll immediately do a check, no need to communicate extra.
            return;
        }

        await Helper.ConfigureAwaitFalse();

        int actualTravelSteps = (args.State.TravelUnitsRemaining + _player.MovementSpeed - 1) / _player.MovementSpeed;
        TimeSpan medianStepInterval = (_maxStepInterval + _minStepInterval) / 2;
        if (args.State.ReasonsToReset.HasFlag(ResetReasons.FasterTravelTime))
        {
            await _client.SayAsync($"Moving to {args.State.DestinationRegion} (remaining: ~{(actualTravelSteps * medianStepInterval).FormatMyWay()} after game reset)", cancellationToken);
        }
        else
        {
            await _client.SayAsync($"Moving to {args.State.DestinationRegion} (remaining: ~{(actualTravelSteps * medianStepInterval).FormatMyWay()})", cancellationToken);
        }
    }

    private async ValueTask OnMovedToRegionAsync(object? sender, MovedToRegionEventArgs args, CancellationToken cancellationToken)
    {
        if (args.TotalTravelUnits == 0)
        {
            // we'll immediately do a check, no need to communicate extra.
            return;
        }

        await Helper.ConfigureAwaitFalse();
        await _client.SayAsync($"Arrived at {args.State.CurrentRegion}.", cancellationToken);
    }

    private async ValueTask OnAurasAddedAsync(object? sender, AurasAddedEventArgs aurasAdded, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        foreach (Aura aura in aurasAdded.AddedAuras)
        {
            string causedBy = _myItemNamesById![aura.CausedByItem];
            if (aura.IsBeneficial)
            {
                await _client.SayAsync($"{causedBy} makes me feel good!", cancellationToken);
            }
            else
            {
                await _client.SayAsync($"{causedBy} makes me feel bad...", cancellationToken);
            }
        }
    }

    private async ValueTask OnAurasExpiredAsync(object? sender, AurasExpiredEventArgs aurasExpired, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        foreach (Aura aura in aurasExpired.ExpiredAuras)
        {
            string causedBy = _myItemNamesById![aura.CausedByItem];
            if (aura.IsBeneficial)
            {
                await _client.SayAsync($"{causedBy} wore off. Darn...", cancellationToken);
            }
            else
            {
                await _client.SayAsync($"{causedBy} wore off. Yay!", cancellationToken);
            }
        }
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

    private ItemType Classify(ItemModel item)
    {
        ItemType itemType;
        if (item.Flags.HasFlag(ArchipelagoItemFlags.LogicalAdvancement))
        {
            if (!s_keyItemTypeByName.TryGetValue(_myItemNamesById![item.Item], out itemType))
            {
                itemType = ItemType.Rat;
            }
        }
        else if (item.Flags.HasFlag(ArchipelagoItemFlags.ImportantNonAdvancement))
        {
            itemType = ItemType.Useful;
        }
        else if (item.Flags.HasFlag(ArchipelagoItemFlags.Trap))
        {
            itemType = ItemType.Trap;
        }
        else
        {
            itemType = ItemType.Filler;
        }

        return itemType;
    }

    private TimeSpan NextStepInterval()
    {
        long tickRange = _maxStepInterval.Ticks - _minStepInterval.Ticks;
        long extraTicks = (long)(Random.Shared.NextDouble() * tickRange);
        return _minStepInterval + TimeSpan.FromTicks(extraTicks);
    }
}
