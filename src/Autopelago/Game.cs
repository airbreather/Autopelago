using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

using ArchipelagoClientDotNet;

[Flags]
public enum ResetReasons
{
    None = 0b0,

    FasterTravelTime = 0b1,
}

// anything that's primarily tracked by the Archipelago server is excluded from this record, e.g.:
// 1. received items
// 2. checked locations
// 3. hints received
public sealed record ResumableGameState
{
    public static readonly ResumableGameState Initial = new()
    {
        StepCount = 0,
        CurrentRegion = Region.BeforeBasketball,
        ConsecutiveFailureCount = 0,
        ReasonsToReset = ResetReasons.None,
        SourceRegion = null,
        DestinationRegion = null,
        TravelUnitsRemaining = 0,
        Auras = [],
        StartedNextStep = false,
    };

    public required long StepCount { get; init; }

    public required Region CurrentRegion { get; init; }

    public required int ConsecutiveFailureCount { get; init; }

    public required ResetReasons ReasonsToReset { get; init; }

    public required Region? SourceRegion { get; init; }

    public required Region? DestinationRegion { get; init; }

    public required int TravelUnitsRemaining { get; init; }

    public required ImmutableArray<Aura> Auras { get; init; }

    public required bool StartedNextStep { get; init; }

    [JsonIgnore]
    public bool AchievedGoal => CurrentRegion == Region.CompletedGoal;

    [JsonIgnore]
    public double StepIntervalMultiplier
    {
        get
        {
            long excludeIfExpiredBy = StepCount + (StartedNextStep ? 1 : 0);
            double result = 1;
            foreach (Aura aura in Auras)
            {
                if (aura.MaxStepCountOnExpiration <= excludeIfExpiredBy)
                {
                    continue;
                }

                if (aura is StepIntervalModifierAura modifierAura)
                {
                    result *= modifierAura.Modifier;
                }
            }

            return result;
        }
    }
}

[JsonPolymorphic]
[JsonDerivedType(typeof(StepIntervalModifierAura), "stepInterval")]
public abstract record Aura
{
    public required long CausedByItem { get; init; }

    public required long StepCountWhenAdded { get; init; }

    // this is a MAXIMUM. hypothetically, some auras could expire earlier, e.g., if they blow their
    // load as a one-shot in response to a trigger.
    public required long MaxStepCountOnExpiration { get; init; }

    [JsonIgnore]
    public abstract bool IsBeneficial { get; }
}

public sealed record StepIntervalModifierAura : Aura
{
    public required double Modifier { get; init; }

    public override bool IsBeneficial => Modifier < 1;
}

public abstract record GameStateUpdateEventArgs
{
    public required GameDifficultySettings DifficultySettings { get; init; }

    public required ResumableGameState State { get; init; }
}

public sealed record MovingToRegionEventArgs : GameStateUpdateEventArgs
{
    public required int TotalTravelUnits { get; init; }
}

public sealed record MovedToRegionEventArgs : GameStateUpdateEventArgs
{
    public required int TotalTravelUnits { get; init; }
}

public sealed record CompletedLocationCheckEventArgs : GameStateUpdateEventArgs
{
    public required long Location { get; init; }
}

public sealed record FailedLocationCheckEventArgs : GameStateUpdateEventArgs
{
    public required long Location { get; init; }
}

public sealed record ResetGameEventArgs : GameStateUpdateEventArgs
{
}

public sealed record AurasAddedEventArgs : GameStateUpdateEventArgs
{
    public ImmutableArray<Aura> AddedAuras { get; init; }
}

public sealed record AurasExpiredEventArgs : GameStateUpdateEventArgs
{
    public ImmutableArray<Aura> ExpiredAuras { get; init; }
}

public sealed class Game(GameDifficultySettings difficultySettings, int seed)
{
    // "non-key item" location count tuning
    private static readonly Dictionary<Region, int> s_numLocationsIn = new()
    {
        [Region.BeforeBasketball] = 40,
        [Region.BeforeMinotaur] = 10,
        [Region.BeforePrawnStars] = 10,
        [Region.BeforeRestaurant] = 10,
        [Region.BeforePirateBakeSale] = 10,
        [Region.AfterRestaurant] = 10,
        [Region.AfterPirateBakeSale] = 10,
        [Region.BeforeGoldfish] = 20,
    };

    // location IDs
    private const long BASE_ID = 300000;

    // "key item" locations
    private static readonly long[] s_locationsBeforeBasketball = Enumerable.Range(0, s_numLocationsIn[Region.BeforeBasketball]).Select(id => BASE_ID + id).ToArray();

    private static readonly long s_locationBasketball = s_locationsBeforeBasketball[^1] + 1;

    private static readonly long[] s_locationsBeforeMinotaur = Enumerable.Range(1, s_numLocationsIn[Region.BeforeMinotaur]).Select(id => s_locationBasketball + id).ToArray();

    private static readonly long[] s_locationsBeforePrawnStars = Enumerable.Range(1, s_numLocationsIn[Region.BeforePrawnStars]).Select(id => s_locationsBeforeMinotaur[^1] + id).ToArray();

    private static readonly long s_locationMinotaur = s_locationsBeforePrawnStars[^1] + 1;

    private static readonly long s_locationPrawnStars = s_locationMinotaur + 1;

    private static readonly long[] s_locationsBeforeRestaurant = Enumerable.Range(1, s_numLocationsIn[Region.BeforeRestaurant]).Select(id => s_locationPrawnStars + id).ToArray();

    private static readonly long[] s_locationsBeforePirateBakeSale = Enumerable.Range(1, s_numLocationsIn[Region.BeforePirateBakeSale]).Select(id => s_locationsBeforeRestaurant[^1] + id).ToArray();

    private static readonly long s_locationRestaurant = s_locationsBeforePirateBakeSale[^1] + 1;

    private static readonly long s_locationPirateBakeSale = s_locationRestaurant + 1;

    private static readonly long[] s_locationsAfterRestaurant = Enumerable.Range(1, s_numLocationsIn[Region.AfterRestaurant]).Select(id => s_locationPirateBakeSale + id).ToArray();

    private static readonly long[] s_locationsAfterPirateBakeSale = Enumerable.Range(1, s_numLocationsIn[Region.AfterPirateBakeSale]).Select(id => s_locationsAfterRestaurant[^1] + id).ToArray();

    private static readonly long s_locationBowlingBallDoor = s_locationsAfterPirateBakeSale[^1] + 1;

    private static readonly long[] s_locationsBeforeGoldfish = Enumerable.Range(1, s_numLocationsIn[Region.BeforeGoldfish]).Select(id => s_locationBowlingBallDoor + id).ToArray();

    private static readonly long s_locationGoldfish = s_locationsBeforeGoldfish[^1] + 1;

    private static readonly Dictionary<(Region S, Region T), int> s_regionDistances = ToComplete(ToUndirected(new()
    {
        [Region.BeforeBasketball] = new() { [Region.Basketball] = false },
        [Region.Basketball] = new()
        {
            [Region.BeforeMinotaur] = true,
            [Region.BeforePrawnStars] = true,
        },
        [Region.BeforeMinotaur] = new() { [Region.Minotaur] = false },
        [Region.BeforePrawnStars] = new() { [Region.PrawnStars] = false },
        [Region.Minotaur] = new() { [Region.BeforeRestaurant] = true },
        [Region.PrawnStars] = new() { [Region.BeforePirateBakeSale] = true },
        [Region.BeforeRestaurant] = new() { [Region.Restaurant] = false },
        [Region.BeforePirateBakeSale] = new() { [Region.PirateBakeSale] = false },
        [Region.Restaurant] = new() { [Region.AfterRestaurant] = true },
        [Region.PirateBakeSale] = new() { [Region.AfterPirateBakeSale] = true },
        [Region.AfterRestaurant] = new() { [Region.BowlingBallDoor] = false },
        [Region.AfterPirateBakeSale] = new() { [Region.BowlingBallDoor] = false },
        [Region.BowlingBallDoor] = new() { [Region.BeforeGoldfish] = true },
        [Region.BeforeGoldfish] = new() { [Region.Goldfish] = false },
        [Region.Goldfish] = [],
    }));

    public static readonly Dictionary<Region, long[]> s_locationsByRegion = new()
    {
        [Region.BeforeBasketball] = s_locationsBeforeBasketball,
        [Region.Basketball] = [s_locationBasketball],
        [Region.BeforeMinotaur] = s_locationsBeforeMinotaur,
        [Region.BeforePrawnStars] = s_locationsBeforePrawnStars,
        [Region.Minotaur] = [s_locationMinotaur],
        [Region.PrawnStars] = [s_locationPrawnStars],
        [Region.BeforeRestaurant] = s_locationsBeforeRestaurant,
        [Region.BeforePirateBakeSale] = s_locationsBeforePirateBakeSale,
        [Region.Restaurant] = [s_locationRestaurant],
        [Region.PirateBakeSale] = [s_locationPirateBakeSale],
        [Region.AfterRestaurant] = s_locationsAfterRestaurant,
        [Region.AfterPirateBakeSale] = s_locationsAfterPirateBakeSale,
        [Region.BowlingBallDoor] = [s_locationBowlingBallDoor],
        [Region.BeforeGoldfish] = s_locationsBeforeGoldfish,
        [Region.Goldfish] = [s_locationGoldfish],
    };

    public static readonly Dictionary<long, Region> s_regionByLocation =
    (
        from kvp in s_locationsByRegion
        from location in kvp.Value
        select (location, kvp.Key)
    ).ToDictionary();

    private readonly Random _random = new(seed);

    private readonly AsyncEvent<MovingToRegionEventArgs> _movingToRegionEvent = new();

    private readonly AsyncEvent<MovedToRegionEventArgs> _movedToRegionEvent = new();

    private readonly AsyncEvent<CompletedLocationCheckEventArgs> _completedLocationCheckEvent = new();

    private readonly AsyncEvent<FailedLocationCheckEventArgs> _failedLocationCheckEvent = new();

    private readonly AsyncEvent<ResetGameEventArgs> _resetGameEvent = new();

    private readonly AsyncEvent<AurasAddedEventArgs> _aurasAddedEvent = new();

    private readonly AsyncEvent<AurasExpiredEventArgs> _aurasExpiredEvent = new();

    private readonly Dictionary<long, ItemType> _receivedItems = [];

    private readonly Dictionary<Region, List<long>> _remainingLocationsInRegion = s_locationsByRegion.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Reverse().ToList());

    private bool _beforeFirstStep = true;

    private int _numNormalRatsReceived;

    public ResumableGameState State { get; private set; } = ResumableGameState.Initial;

    public int RatCount => _receivedItems.Values.Sum(i => i switch
        {
            ItemType.OneNamedRat => 1,
            ItemType.EntireRatPack => 5,
            _ => 0,
        }) + _numNormalRatsReceived;

    public bool IsCompleted => State.CurrentRegion == Region.CompletedGoal || _receivedItems.ContainsValue(ItemType.Goal);

    public event AsyncEventHandler<MovingToRegionEventArgs> MovingToRegion
    {
        add => _movingToRegionEvent.Add(value);
        remove => _movingToRegionEvent.Remove(value);
    }

    public event AsyncEventHandler<MovedToRegionEventArgs> MovedToRegion
    {
        add => _movedToRegionEvent.Add(value);
        remove => _movedToRegionEvent.Remove(value);
    }

    public event AsyncEventHandler<CompletedLocationCheckEventArgs> CompletedLocationCheck
    {
        add => _completedLocationCheckEvent.Add(value);
        remove => _completedLocationCheckEvent.Remove(value);
    }

    public event AsyncEventHandler<FailedLocationCheckEventArgs> FailedLocationCheck
    {
        add => _failedLocationCheckEvent.Add(value);
        remove => _failedLocationCheckEvent.Remove(value);
    }

    public event AsyncEventHandler<ResetGameEventArgs> ResetGame
    {
        add => _resetGameEvent.Add(value);
        remove => _resetGameEvent.Remove(value);
    }

    public event AsyncEventHandler<AurasAddedEventArgs> AurasAdded
    {
        add => _aurasAddedEvent.Add(value);
        remove => _aurasAddedEvent.Remove(value);
    }

    public event AsyncEventHandler<AurasExpiredEventArgs> AurasExpired
    {
        add => _aurasExpiredEvent.Add(value);
        remove => _aurasExpiredEvent.Remove(value);
    }

    public void InitState(ResumableGameState state)
    {
        if (!_beforeFirstStep)
        {
            throw new InvalidOperationException("Init* methods may only be called before StepAsync.");
        }

        State = state;
    }

    public async ValueTask<bool> StepAsync(Player player, CancellationToken cancellationToken = default)
    {
        _beforeFirstStep = false;
        await Helper.ConfigureAwaitFalse();
        State = State with { StartedNextStep = true };
        bool innerResult = await Inner();
        State = State with { StartedNextStep = false, StepCount = State.StepCount + 1 };
        ImmutableArray<Aura> expiredAuras = State.Auras.RemoveAll(a => a.MaxStepCountOnExpiration > State.StepCount);
        if (!expiredAuras.IsEmpty)
        {
            State = State with { Auras = State.Auras.RemoveRange(expiredAuras) };
            await _aurasExpiredEvent.InvokeAsync(this, new() { DifficultySettings = difficultySettings, State = State, ExpiredAuras = expiredAuras }, cancellationToken);
        }

        return innerResult;

        async ValueTask<bool> Inner()
        {
            if (IsCompleted)
            {
                return false;
            }

            await Helper.ConfigureAwaitFalse();

            if (State.ReasonsToReset != ResetReasons.None)
            {
                await _resetGameEvent.InvokeAsync(this, new() { DifficultySettings = difficultySettings, State = State }, cancellationToken);
                State = State with { ReasonsToReset = ResetReasons.None };
                return true;
            }

            if (State.CurrentRegion == Region.Traveling)
            {
                await TravelStepAsync(player, cancellationToken);
                return true;
            }

            if (await StartTravelingIfNeeded(player, cancellationToken))
            {
                await TravelStepAsync(player, cancellationToken);
                return true;
            }

            if (_remainingLocationsInRegion[State.CurrentRegion].Count == 0)
            {
                return false;
            }

            await TryNextCheckAsync(player, cancellationToken);
            if (IsCompleted)
            {
                return false;
            }

            await StartTravelingIfNeeded(player, cancellationToken);
            return true;
        }
    }

    public void MarkLocationChecked(long locationId)
    {
        _remainingLocationsInRegion[s_regionByLocation[locationId]].Remove(locationId);
    }

    public async ValueTask ReceiveItem(ItemModel item, string itemName, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        ItemType itemType = Classify(item.Flags, itemName);
        if (!_receivedItems.TryAdd(item.Item, itemType))
        {
            if (_receivedItems[item.Item] != itemType)
            {
                throw new InvalidDataException("Item was received multiple times, with different ItemType values");
            }

            if (itemType == ItemType.OneNormalRat)
            {
                ++_numNormalRatsReceived;
            }

            return;
        }

        StepIntervalModifierAura? auraToAdd = null;
        switch (itemType)
        {
            case ItemType.Trap:
                auraToAdd = new() { CausedByItem = item.Item, StepCountWhenAdded = State.StepCount, MaxStepCountOnExpiration = State.StepCount + 2, Modifier = 2 };
                break;

            case ItemType.Useful:
                auraToAdd = new() { CausedByItem = item.Item, StepCountWhenAdded = State.StepCount, MaxStepCountOnExpiration = State.StepCount + 8, Modifier = 0.5 };
                break;
        }

        if (auraToAdd is null)
        {
            return;
        }

        State = State with { Auras = [..State.Auras, auraToAdd] };
        await _aurasAddedEvent.InvokeAsync(this, new() { DifficultySettings = difficultySettings, State = State, AddedAuras = [auraToAdd] }, cancellationToken);
    }

    private static Dictionary<(Region S, Region T), int> ToComplete(Dictionary<Region, Dictionary<Region, bool>> g)
    {
        Region[] keys = [..g.Keys];
        Dictionary<(Region S, Region T), int> result = (
            from s in keys
            let ts = g[s]
            from t in keys
            select ((s, t), s == t ? 0 : ts.TryGetValue(t, out bool w) ? w ? 1 : 0 : int.MaxValue / 2)
        ).ToDictionary();
        foreach (Region k in keys)
        {
            foreach (Region i in keys)
            {
                foreach (Region j in keys)
                {
                    ref int slot = ref CollectionsMarshal.GetValueRefOrNullRef(result, (i, j));
                    int candidate = result[(i, k)] + result[(k, j)];
                    if (slot > candidate)
                    {
                        slot = candidate;
                    }
                }
            }
        }

        return result;
    }

    private static Dictionary<Region, Dictionary<Region, bool>> ToUndirected(Dictionary<Region, Dictionary<Region, bool>> g)
    {
        foreach ((Region s, Dictionary<Region, bool> ts) in g.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToDictionary()))
        {
            foreach (Region t in ts.Keys)
            {
                g[t][s] = true; // the only freebies are going from "AfterFooBeforeBar" to "Bar".
            }
        }

        return g;
    }

    private static ItemType Classify(ArchipelagoItemFlags flags, string itemName)
    {
        if (flags.HasFlag(ArchipelagoItemFlags.Trap))
        {
            return ItemType.Trap;
        }

        if (flags.HasFlag(ArchipelagoItemFlags.ImportantNonAdvancement))
        {
            return ItemType.Useful;
        }

        if (!flags.HasFlag(ArchipelagoItemFlags.LogicalAdvancement))
        {
            return ItemType.Filler;
        }

        return itemName switch
        {
            "Normal Rat"
                => ItemType.OneNormalRat,

            "Pack Rat" or "Pizza Rat" or "Chef Rat" or "Ninja Rat" or "Gym Rat" or "Computer Rat" or "Pie Rat" or "Ziggu Rat" or "Acro Rat" or "Lab Rat" or "Soc-Rat-es"
                => ItemType.OneNamedRat,

            "Entire Rat Pack"
                => ItemType.EntireRatPack,

            "Red Matador's Cape"
                => ItemType.UnlocksMinotaur,

            "Premium Can of Prawn Food"
                => ItemType.UnlocksPrawnStars,

            "A Cookie"
                => ItemType.UnlocksRestaurant,

            "Bribe"
                => ItemType.UnlocksPirateBakeSale,

            "Masterful Longsword"
                => ItemType.UnlocksGoldfish,

            "Lockheed SR-71 Blackbird"
                => ItemType.Goal,

            _ => throw new InvalidDataException("All items should have been accounted for above."),
        };
    }

    private async ValueTask TryNextCheckAsync(Player player, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        List<long> remainingLocations = _remainingLocationsInRegion[State.CurrentRegion];
        long locationId = remainingLocations[^1];

        int roll = NextD20(player, player.DiceModifier[State.CurrentRegion]);
        if (roll < difficultySettings.DifficultyClass[State.CurrentRegion])
        {
            State = State with { ConsecutiveFailureCount = State.ConsecutiveFailureCount + 1 };
            await _failedLocationCheckEvent.InvokeAsync(this, new() { DifficultySettings = difficultySettings, State = State, Location = locationId }, cancellationToken);
            return;
        }

        await _completedLocationCheckEvent.InvokeAsync(this, new() { DifficultySettings = difficultySettings, State = State, Location = locationId }, cancellationToken);
        State = State with { ConsecutiveFailureCount = 0 };
        remainingLocations.RemoveAt(remainingLocations.Count - 1);
        if (State.CurrentRegion == Region.Goldfish)
        {
            State = State with { CurrentRegion = Region.Traveling, SourceRegion = Region.Goldfish, DestinationRegion = Region.CompletedGoal, ReasonsToReset = ResetReasons.None };
            await _movingToRegionEvent.InvokeAsync(this, new() { DifficultySettings = difficultySettings, State = State, TotalTravelUnits = 0 }, cancellationToken);
            State = State with { CurrentRegion = Region.CompletedGoal, SourceRegion = Region.Goldfish, DestinationRegion = Region.CompletedGoal };
            await _movedToRegionEvent.InvokeAsync(this, new() { DifficultySettings = difficultySettings, State = State, TotalTravelUnits = 0 }, cancellationToken);
            State = State with { SourceRegion = null, DestinationRegion = null };
        }
    }

    private async ValueTask<bool> StartTravelingIfNeeded(Player player, CancellationToken cancellationToken)
    {
        Region bestNextRegion = DetermineNextRegion(player);
        if (bestNextRegion == State.CurrentRegion)
        {
            return false;
        }

        int travelUnitsFromCurrentRegion = s_regionDistances[(State.CurrentRegion, bestNextRegion)] * difficultySettings.RegionChangeSteps;
        int travelUnitsFromMenu = s_regionDistances[(Region.BeforeBasketball, bestNextRegion)] * difficultySettings.RegionChangeSteps;
        int travelUnitsRemaining;
        ResetReasons resetReasons = State.ReasonsToReset;
        if (travelUnitsFromMenu + player.MovementSpeed < travelUnitsFromCurrentRegion)
        {
            travelUnitsRemaining = travelUnitsFromMenu;
            resetReasons |= ResetReasons.FasterTravelTime;
        }
        else
        {
            travelUnitsRemaining = travelUnitsFromCurrentRegion;
        }

        State = State with { CurrentRegion = Region.Traveling, SourceRegion = State.CurrentRegion, DestinationRegion = bestNextRegion, TravelUnitsRemaining = travelUnitsRemaining, ReasonsToReset = resetReasons };
        await _movingToRegionEvent.InvokeAsync(this, new() { DifficultySettings = difficultySettings, State = State, TotalTravelUnits = travelUnitsRemaining }, cancellationToken);
        if (travelUnitsRemaining > 0)
        {
            return true;
        }

        State = State with { CurrentRegion = bestNextRegion };
        await _movedToRegionEvent.InvokeAsync(this, new() { DifficultySettings = difficultySettings, State = State, TotalTravelUnits = 0 }, cancellationToken);
        State = State with { SourceRegion = null, DestinationRegion = null };
        return false;
    }

    private async ValueTask TravelStepAsync(Player player, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        int totalTravelUnits = difficultySettings.RegionChangeSteps * Math.Min(
            s_regionDistances[(State.SourceRegion!.Value, State.DestinationRegion!.Value)],
            s_regionDistances[(Region.BeforeBasketball, State.DestinationRegion!.Value)]);

        State = State with { TravelUnitsRemaining = State.TravelUnitsRemaining - player.MovementSpeed };
        if (State.TravelUnitsRemaining >= 0)
        {
            await _movingToRegionEvent.InvokeAsync(this, new() { DifficultySettings = difficultySettings, State = State, TotalTravelUnits = totalTravelUnits }, cancellationToken);
            return;
        }

        State = State with { CurrentRegion = State.DestinationRegion!.Value, TravelUnitsRemaining = 0 };
        await _movedToRegionEvent.InvokeAsync(this, new() { DifficultySettings = difficultySettings, State = State, TotalTravelUnits = totalTravelUnits }, cancellationToken);
        State = State with { SourceRegion = null, DestinationRegion = null };
    }

    private int NextD20(Player player, int baseDiceModifier)
    {
        return _random.Next(1, 21) + baseDiceModifier + State.ConsecutiveFailureCount / player.ConsecutiveFailuresBeforeDiceModifierIncrement;
    }

    private Region DetermineNextRegion(Player player)
    {
        int ratCount = RatCount;

        // ASSUMPTION: you don't need help to figure out what to do in Traveling or CompletedGoal.

        Region bestRegion = State.CurrentRegion;
        int bestRegionDifficultyClass = EffectiveDifficultyClass(bestRegion);
        int bestRegionDistance = 0;

        // if your current region is empty, then you should favor moving ANYWHERE else.
        if (_remainingLocationsInRegion[bestRegion].Count == 0)
        {
            bestRegionDifficultyClass = int.MaxValue;
        }

        HandleUnlockedRegion(Region.BeforeBasketball);

        if (ratCount < 5)
        {
            return bestRegion;
        }

        HandleUnlockedRegion(Region.Basketball);
        if (_remainingLocationsInRegion[Region.Basketball].Count > 0)
        {
            return bestRegion;
        }

        HandleUnlockedRegion(Region.BeforeMinotaur);
        HandleUnlockedRegion(Region.BeforePrawnStars);

        if (_receivedItems.ContainsValue(ItemType.UnlocksMinotaur))
        {
            HandleUnlockedRegion(Region.Minotaur);
        }

        if (_receivedItems.ContainsValue(ItemType.UnlocksPrawnStars))
        {
            HandleUnlockedRegion(Region.PrawnStars);
        }

        bool pastMinotaur = _remainingLocationsInRegion[Region.Minotaur].Count == 0;
        bool pastPrawnStars = _remainingLocationsInRegion[Region.PrawnStars].Count == 0;
        if (!(pastMinotaur || pastPrawnStars))
        {
            return bestRegion;
        }

        if (pastMinotaur)
        {
            HandleUnlockedRegion(Region.BeforeRestaurant);
            if (_receivedItems.ContainsValue(ItemType.UnlocksRestaurant))
            {
                HandleUnlockedRegion(Region.Restaurant);
            }
        }

        if (pastPrawnStars)
        {
            HandleUnlockedRegion(Region.BeforePirateBakeSale);
            if (_receivedItems.ContainsValue(ItemType.UnlocksPirateBakeSale))
            {
                HandleUnlockedRegion(Region.PirateBakeSale);
            }
        }

        bool pastRestaurant = _remainingLocationsInRegion[Region.Restaurant].Count == 0;
        bool pastPirateBakeSale = _remainingLocationsInRegion[Region.PirateBakeSale].Count == 0;
        if (!(pastRestaurant || pastPirateBakeSale))
        {
            return bestRegion;
        }

        if (pastRestaurant)
        {
            HandleUnlockedRegion(Region.AfterRestaurant);
        }

        if (pastPirateBakeSale)
        {
            HandleUnlockedRegion(Region.AfterPirateBakeSale);
        }

        if (ratCount < 20)
        {
            return bestRegion;
        }

        HandleUnlockedRegion(Region.BowlingBallDoor);
        if (_remainingLocationsInRegion[Region.BowlingBallDoor].Count > 0)
        {
            return bestRegion;
        }

        HandleUnlockedRegion(Region.BeforeGoldfish);
        if (!_receivedItems.ContainsValue(ItemType.UnlocksGoldfish))
        {
            return bestRegion;
        }

        HandleUnlockedRegion(Region.Goldfish);
        return bestRegion;

        void HandleUnlockedRegion(Region testRegion)
        {
            if (_remainingLocationsInRegion[testRegion].Count == 0)
            {
                return;
            }

            int testDifficultyClass = EffectiveDifficultyClass(testRegion);
            if (testRegion == Region.Goldfish ||
                testDifficultyClass < bestRegionDifficultyClass ||
                (testDifficultyClass == bestRegionDifficultyClass && s_regionDistances[(State.CurrentRegion, testRegion)] < bestRegionDistance))
            {
                bestRegion = testRegion;
                bestRegionDifficultyClass = testDifficultyClass;
                bestRegionDistance = s_regionDistances[(State.CurrentRegion, bestRegion)];
            }
        }

        int EffectiveDifficultyClass(Region region) => difficultySettings.DifficultyClass[region] - player.DiceModifier[region];
    }
}
