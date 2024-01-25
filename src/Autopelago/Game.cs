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
        CurrentRegion = Region.Before8Rats,
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
        [Region.Before8Rats] = 40,
        [Region.After8RatsBeforeA] = 10,
        [Region.After8RatsBeforeB] = 10,
        [Region.AfterABeforeC] = 10,
        [Region.AfterBBeforeD] = 10,
        [Region.AfterCBefore20Rats] = 10,
        [Region.AfterDBefore20Rats] = 10,
        [Region.After20RatsBeforeE] = 20,
        [Region.After20RatsBeforeF] = 20,
    };

    // location IDs
    private const long BASE_ID = 300000;

    // "key item" locations
    private static readonly long[] s_locationsBefore8Rats = Enumerable.Range(0, s_numLocationsIn[Region.Before8Rats]).Select(id => BASE_ID + id).ToArray();

    private static readonly long s_locationGate8Rats = s_locationsBefore8Rats[^1] + 1;

    private static readonly long[] s_locationsAfter8RatsBeforeA = Enumerable.Range(1, s_numLocationsIn[Region.After8RatsBeforeA]).Select(id => s_locationGate8Rats + id).ToArray();

    private static readonly long[] s_locationsAfter8RatsBeforeB = Enumerable.Range(1, s_numLocationsIn[Region.After8RatsBeforeB]).Select(id => s_locationsAfter8RatsBeforeA[^1] + id).ToArray();

    private static readonly long s_locationA = s_locationsAfter8RatsBeforeB[^1] + 1;

    private static readonly long s_locationB = s_locationA + 1;

    private static readonly long[] s_locationsAfterABeforeC = Enumerable.Range(1, s_numLocationsIn[Region.AfterABeforeC]).Select(id => s_locationB + id).ToArray();

    private static readonly long[] s_locationsAfterBBeforeD = Enumerable.Range(1, s_numLocationsIn[Region.AfterBBeforeD]).Select(id => s_locationsAfterABeforeC[^1] + id).ToArray();

    private static readonly long s_locationC = s_locationsAfterBBeforeD[^1] + 1;

    private static readonly long s_locationD = s_locationC + 1;

    private static readonly long[] s_locationsAfterCBefore20Rats = Enumerable.Range(1, s_numLocationsIn[Region.AfterCBefore20Rats]).Select(id => s_locationD + id).ToArray();

    private static readonly long[] s_locationsAfterDBefore20Rats = Enumerable.Range(1, s_numLocationsIn[Region.AfterDBefore20Rats]).Select(id => s_locationsAfterCBefore20Rats[^1] + id).ToArray();

    private static readonly long s_locationGate20Rats = s_locationsAfterDBefore20Rats[^1] + 1;

    private static readonly long[] s_locationsAfter20RatsBeforeE = Enumerable.Range(1, s_numLocationsIn[Region.After20RatsBeforeE]).Select(id => s_locationGate20Rats + id).ToArray();

    private static readonly long[] s_locationsAfter20RatsBeforeF = Enumerable.Range(1, s_numLocationsIn[Region.After20RatsBeforeF]).Select(id => s_locationsAfter20RatsBeforeE[^1] + id).ToArray();

    private static readonly long s_locationE = s_locationsAfter20RatsBeforeF[^1] + 1;

    private static readonly long s_locationF = s_locationE + 1;

    private static readonly long s_locationGoal = s_locationF + 1;

    private static readonly Dictionary<(Region S, Region T), int> s_regionDistances = ToComplete(ToUndirected(new()
    {
        [Region.Before8Rats] = new() { [Region.Gate8Rats] = false },
        [Region.Gate8Rats] = new()
        {
            [Region.After8RatsBeforeA] = true,
            [Region.After8RatsBeforeB] = true,
        },
        [Region.After8RatsBeforeA] = new() { [Region.A] = false },
        [Region.After8RatsBeforeB] = new() { [Region.B] = false },
        [Region.A] = new() { [Region.AfterABeforeC] = true },
        [Region.B] = new() { [Region.AfterBBeforeD] = true },
        [Region.AfterABeforeC] = new() { [Region.C] = false },
        [Region.AfterBBeforeD] = new() { [Region.D] = false },
        [Region.C] = new() { [Region.AfterCBefore20Rats] = true },
        [Region.D] = new() { [Region.AfterDBefore20Rats] = true },
        [Region.AfterCBefore20Rats] = new() { [Region.Gate20Rats] = false },
        [Region.AfterDBefore20Rats] = new() { [Region.Gate20Rats] = false },
        [Region.Gate20Rats] = new()
        {
            [Region.After20RatsBeforeE] = true,
            [Region.After20RatsBeforeF] = true,
        },
        [Region.After20RatsBeforeE] = new() { [Region.E] = false },
        [Region.After20RatsBeforeF] = new() { [Region.F] = false },
        [Region.E] = new() { [Region.TryingForGoal] = true, },
        [Region.F] = new() { [Region.TryingForGoal] = true, },
        [Region.TryingForGoal] = [],
    }));

    public static readonly Dictionary<Region, long[]> s_locationsByRegion = new()
    {
        [Region.Before8Rats] = s_locationsBefore8Rats,
        [Region.Gate8Rats] = [s_locationGate8Rats],
        [Region.After8RatsBeforeA] = s_locationsAfter8RatsBeforeA,
        [Region.After8RatsBeforeB] = s_locationsAfter8RatsBeforeB,
        [Region.A] = [s_locationA],
        [Region.B] = [s_locationB],
        [Region.AfterABeforeC] = s_locationsAfterABeforeC,
        [Region.AfterBBeforeD] = s_locationsAfterBBeforeD,
        [Region.C] = [s_locationC],
        [Region.D] = [s_locationD],
        [Region.AfterCBefore20Rats] = s_locationsAfterCBefore20Rats,
        [Region.AfterDBefore20Rats] = s_locationsAfterDBefore20Rats,
        [Region.Gate20Rats] = [s_locationGate20Rats],
        [Region.After20RatsBeforeE] = s_locationsAfter20RatsBeforeE,
        [Region.After20RatsBeforeF] = s_locationsAfter20RatsBeforeF,
        [Region.E] = [s_locationE],
        [Region.F] = [s_locationF],
        [Region.TryingForGoal] = [s_locationGoal],
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

    public async ValueTask ReceiveItem(long itemId, ItemType itemType, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        if (!_receivedItems.TryAdd(itemId, itemType))
        {
            if (_receivedItems[itemId] != itemType)
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
                auraToAdd = new() { CausedByItem = itemId, StepCountWhenAdded = State.StepCount, MaxStepCountOnExpiration = State.StepCount + 2, Modifier = 2 };
                break;

            case ItemType.Useful:
                auraToAdd = new() { CausedByItem = itemId, StepCountWhenAdded = State.StepCount, MaxStepCountOnExpiration = State.StepCount + 8, Modifier = 0.5 };
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
        if (State.CurrentRegion == Region.TryingForGoal)
        {
            State = State with { CurrentRegion = Region.Traveling, SourceRegion = Region.TryingForGoal, DestinationRegion = Region.CompletedGoal, ReasonsToReset = ResetReasons.None };
            await _movingToRegionEvent.InvokeAsync(this, new() { DifficultySettings = difficultySettings, State = State, TotalTravelUnits = 0 }, cancellationToken);
            State = State with { CurrentRegion = Region.CompletedGoal, SourceRegion = Region.TryingForGoal, DestinationRegion = Region.CompletedGoal };
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
        int travelUnitsFromMenu = s_regionDistances[(Region.Before8Rats, bestNextRegion)] * difficultySettings.RegionChangeSteps;
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
            s_regionDistances[(Region.Before8Rats, State.DestinationRegion!.Value)]);

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
        //
        // if the goal is open, then you should ALWAYS try for it.
        if (ratCount >= 20 && ( _remainingLocationsInRegion[Region.E].Count == 0 || _remainingLocationsInRegion[Region.F].Count == 0))
        {
            return Region.TryingForGoal;
        }

        Region bestRegion = State.CurrentRegion;
        int bestRegionDifficultyClass = EffectiveDifficultyClass(bestRegion);
        int bestRegionDistance = 0;

        // if your current region is empty, then you should favor moving ANYWHERE else.
        if (_remainingLocationsInRegion[bestRegion].Count == 0)
        {
            bestRegionDifficultyClass = int.MaxValue;
        }

        HandleUnlockedRegion(Region.Before8Rats);

        if (ratCount < 8)
        {
            return bestRegion;
        }

        HandleUnlockedRegion(Region.Gate8Rats);
        if (_remainingLocationsInRegion[Region.Gate8Rats].Count > 0)
        {
            return bestRegion;
        }

        HandleUnlockedRegion(Region.After8RatsBeforeA);
        HandleUnlockedRegion(Region.After8RatsBeforeB);

        if (_receivedItems.ContainsValue(ItemType.A))
        {
            HandleUnlockedRegion(Region.A);
        }

        if (_receivedItems.ContainsValue(ItemType.B))
        {
            HandleUnlockedRegion(Region.B);
        }

        bool pastA = _remainingLocationsInRegion[Region.A].Count > 0;
        bool pastB = _remainingLocationsInRegion[Region.B].Count > 0;
        if (!(pastA || pastB))
        {
            return bestRegion;
        }

        if (pastA)
        {
            HandleUnlockedRegion(Region.AfterABeforeC);
            if (_receivedItems.ContainsValue(ItemType.C))
            {
                HandleUnlockedRegion(Region.C);
            }
        }

        if (pastB)
        {
            HandleUnlockedRegion(Region.AfterBBeforeD);
            if (_receivedItems.ContainsValue(ItemType.D))
            {
                HandleUnlockedRegion(Region.D);
            }
        }

        bool pastC = _remainingLocationsInRegion[Region.C].Count > 0;
        bool pastD = _remainingLocationsInRegion[Region.D].Count > 0;
        if (!(pastC || pastD))
        {
            return bestRegion;
        }

        if (pastC)
        {
            HandleUnlockedRegion(Region.AfterCBefore20Rats);
        }

        if (pastD)
        {
            HandleUnlockedRegion(Region.AfterDBefore20Rats);
        }

        if (ratCount < 20)
        {
            return bestRegion;
        }

        HandleUnlockedRegion(Region.Gate20Rats);
        if (_remainingLocationsInRegion[Region.Gate20Rats].Count > 0)
        {
            return bestRegion;
        }

        HandleUnlockedRegion(Region.After20RatsBeforeE);
        HandleUnlockedRegion(Region.After20RatsBeforeF);
        if (_receivedItems.ContainsValue(ItemType.E))
        {
            HandleUnlockedRegion(Region.E);
        }

        if (_receivedItems.ContainsValue(ItemType.F))
        {
            HandleUnlockedRegion(Region.F);
        }

        return bestRegion;

        void HandleUnlockedRegion(Region testRegion)
        {
            if (_remainingLocationsInRegion[testRegion].Count == 0)
            {
                return;
            }

            int testDifficultyClass = EffectiveDifficultyClass(testRegion);
            if (testDifficultyClass < bestRegionDifficultyClass ||
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
