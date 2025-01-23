using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;

namespace Autopelago;

public enum TargetLocationReason
{
    GameNotStarted,
    NowhereUsefulToMove,
    ClosestReachableUnchecked,
    Priority,
    PriorityPriority,
    GoMode,
    Startled,
}

public enum AddPriorityLocationResult
{
    AlreadyPrioritized,
    AddedReachable,
    AddedUnreachable,
}

public sealed record ServerSavedState
{
    public int FoodFactor { get; init; }

    public int LuckFactor { get; init; }

    public int EnergyFactor { get; init; }

    public int StyleFactor { get; init; }

    public int DistractionCounter { get; init; }

    public int StartledCounter { get; init; }

    public bool HasConfidence { get; init; }

    public int MercyModifier { get; init; }

    public string CurrentLocation { get; init; } = GameDefinitions.Instance[GameDefinitions.Instance.StartLocation].Name;

    public ImmutableArray<string> PriorityPriorityLocations { get; init; } = [];

    public ImmutableArray<string> PriorityLocations { get; init; } = [];
}

public readonly record struct LocationVector
{
    public required LocationKey PreviousLocation { get; init; }

    public required LocationKey CurrentLocation { get; init; }
}

public sealed partial class Game
{
    private const int DefaultActionsPerStep = 3;

    // at most, FoodFactor adds +1
    private const int MaxActionsPerStep = DefaultActionsPerStep + 1;

    private const int MaxMovementsPerAction = 3;

    // at most, EnergyFactor can let you move 2x as much at once.
    private const int MaxMovementsPerStep = MaxActionsPerStep * MaxMovementsPerAction * 2;

    private readonly Lock? _lock;

    private readonly GameInstrumentation? _instrumentation;

    private BitArray128 _hardLockedRegions = new(GameDefinitions.Instance.AllRegions.Length);
    public BitArray128 HardLockedRegionsReadOnly => _hardLockedRegions;

    private BitArray128 _softLockedRegions = new(GameDefinitions.Instance.AllRegions.Length);
    public BitArray128 SoftLockedRegionsReadOnly => _softLockedRegions;

    private BitArray384 _checkedLocations = new(GameDefinitions.Instance.AllLocations.Length);

    private int _actionBalanceAfterPreviousStep;

    private bool _initializedServerSavedState;

    private FrozenDictionary<ArchipelagoItemFlags, BitArray384>? _spoilerData;

    public ReadOnlyCollection<LocationVector> PreviousStepMovementLog { get; }

    public LocationKey CurrentLocation { get; private set; } = GameDefinitions.Instance.StartLocation;

    public LocationKey TargetLocation { get; private set; } = GameDefinitions.Instance.StartLocation;

    public TargetLocationReason TargetLocationReason { get; private set; } = TargetLocationReason.GameNotStarted;

    private bool _receivedItemsInitialized;
    public ReadOnlyCollection<ItemKey> ReceivedItems { get; }

    private bool _checkedLocationsInitialized;
    public BitArray384 LocationIsChecked => _checkedLocations;
    public ReadOnlyCollection<LocationKey> CheckedLocations { get; }

    private BitArray384 _locationIsRelevant;
    public BitArray384 LocationIsRelevant => _locationIsRelevant;

    public ReadOnlyCollection<LocationKey> PriorityPriorityLocations { get; }

    public ReadOnlyCollection<LocationKey> PriorityLocations { get; }

    private LocationKey _victoryLocation = LocationKey.Nonexistent;
    public LocationKey VictoryLocation
    {
        get
        {
            using Lock.Scope _ = EnterLockScope();
            if (_victoryLocation == LocationKey.Nonexistent)
            {
                throw new InvalidOperationException("Game has not started yet.");
            }

            return _victoryLocation;
        }
    }

    public FrozenDictionary<ArchipelagoItemFlags, BitArray384> SpoilerData
    {
        get
        {
            using Lock.Scope _ = EnterLockScope();
            EnsureStarted();
            return _spoilerData!;
        }
    }

    public int FoodFactor { get; private set; }

    public int LuckFactor { get; private set; }

    public int EnergyFactor { get; private set; }

    public int StyleFactor { get; private set; }

    public int DistractionCounter { get; private set; }

    public int StartledCounter { get; private set; }

    public int MercyModifier { get; private set; }

    public bool HasConfidence { get; private set; }

    private int? _ratCount;
    public int RatCount
    {
        get
        {
            using Lock.Scope _ = EnterLockScope();
            EnsureStarted();
            if (_ratCount is null)
            {
                _ratCount = 0;
                ReadOnlySpan<int> receivedItems = _receivedItems;
                foreach (ItemKey item in GameDefinitions.Instance.ItemsWithNonzeroRatCounts)
                {
                    if (receivedItems[item.N] > 0)
                    {
                        _ratCount += GameDefinitions.Instance.AllItems[item.N].RatCount * receivedItems[item.N];
                    }
                }
            }

            return _ratCount.GetValueOrDefault();
        }
    }

    public ServerSavedState ServerSavedState
    {
        get
        {
            using Lock.Scope _ = EnterLockScope();
            if (!_initializedServerSavedState)
            {
                throw new InvalidOperationException("Game has not started yet.");
            }

            return new()
            {
                FoodFactor = FoodFactor,
                LuckFactor = LuckFactor,
                EnergyFactor = EnergyFactor,
                StyleFactor = StyleFactor,
                DistractionCounter = DistractionCounter,
                StartledCounter = StartledCounter,
                MercyModifier = MercyModifier,
                HasConfidence = HasConfidence,
                CurrentLocation = GameDefinitions.Instance[CurrentLocation].Name,
                PriorityPriorityLocations = [.. _priorityPriorityLocations.Select(l => GameDefinitions.Instance[l].Name)],
                PriorityLocations = [.. _priorityLocations.Select(l => GameDefinitions.Instance[l].Name)],
            };
        }
    }

    private Prng.State _prngState;
    public Prng.State PrngState { get => _prngState; set => _prngState = value; }

    public bool HasStarted { get; private set; }

    public bool HasCompletedGoal => _checkedLocations[_victoryLocation.N];

    public bool IsCompleted => _checkedLocations.TrueCount == _locationIsRelevant.TrueCount;

    private static int GetPermanentRollModifier(int ratCount)
    {
        // diminishing returns
        int rolling = 0;

        // +1 for every 3 rats up to the first 12
        if (ratCount <= 12)
        {
            rolling += ratCount / 3;
            return rolling;
        }

        rolling += 4;
        ratCount -= 12;

        // beyond that, +1 for every 5 rats up to the next 15
        if (ratCount <= 15)
        {
            rolling += ratCount / 5;
            return rolling;
        }

        rolling += 3;
        ratCount -= 15;

        // beyond that, +1 for every 7 rats up to the next 14
        if (ratCount <= 14)
        {
            rolling += ratCount / 7;
            return rolling;
        }

        rolling += 2;
        ratCount -= 14;

        // everything else is +1 for every 8 rats.
        rolling += ratCount / 8;
        return rolling;
    }

    private sbyte ModifyRoll(byte d20, int mercy, int multi, bool hasUnlucky, bool hasStylish)
    {
        return checked((sbyte)(
            d20 +
            GetPermanentRollModifier(RatCount) +
            mercy +
            (multi * -5) +
            (hasUnlucky ? -5 : 0) +
            (hasStylish ? 5 : 0)
        ));
    }

    public void ArbitrarilyModifyState<T>(Expression<Func<Game, T>> prop, T value)
    {
        ((PropertyInfo)((MemberExpression)prop.Body).Member).SetValue(this, value);
    }

    public void InitializeCheckedLocations(IEnumerable<LocationKey> checkedLocations)
    {
        using Lock.Scope _ = EnterLockScope();
        if (_checkedLocationsInitialized)
        {
            throw new InvalidOperationException("Checked locations have already been initialized.");
        }

        foreach (LocationKey location in checkedLocations)
        {
            if (_checkedLocations[location.N])
            {
                continue;
            }

            _checkedLocations[location.N] = true;
            _checkedLocationsOrder.Add(location);
            --_regionUncheckedLocationsCount[GameDefinitions.Instance.Region[location].N];
        }

        _checkedLocationsInitialized = true;
    }

    public void InitializeReceivedItems(IEnumerable<ItemKey> receivedItems)
    {
        using Lock.Scope _ = EnterLockScope();
        if (_receivedItemsInitialized)
        {
            throw new InvalidOperationException("Received items have already been initialized.");
        }

        Span<int> receivedItemsSpan = _receivedItems;
        foreach (ItemKey item in receivedItems)
        {
            ++receivedItemsSpan[item.N];
            _receivedItemsOrder.Add(item);
        }

        _ratCount = null;
        _receivedItemsInitialized = true;
    }

    public void InitializeSpoilerData(FrozenDictionary<ArchipelagoItemFlags, BitArray384> spoilerData)
    {
        using Lock.Scope _ = EnterLockScope();
        if (_spoilerData is not null)
        {
            throw new InvalidOperationException("Spoiler data has already been initialized.");
        }

        _spoilerData = spoilerData;
    }

    public void InitializeServerSavedState(ServerSavedState serverSavedState)
    {
        using Lock.Scope _ = EnterLockScope();
        if (_initializedServerSavedState)
        {
            throw new InvalidOperationException("Server saved state has already been initialized.");
        }

        FoodFactor = serverSavedState.FoodFactor;
        LuckFactor = serverSavedState.LuckFactor;
        EnergyFactor = serverSavedState.EnergyFactor;
        StyleFactor = serverSavedState.StyleFactor;
        DistractionCounter = serverSavedState.DistractionCounter;
        StartledCounter = serverSavedState.StartledCounter;
        MercyModifier = serverSavedState.MercyModifier;
        HasConfidence = serverSavedState.HasConfidence;
        CurrentLocation = GameDefinitions.Instance.LocationsByName[serverSavedState.CurrentLocation];
        _prevMovementLog.Clear();
        _prevMovementLog.Add(new()
        {
            PreviousLocation = CurrentLocation,
            CurrentLocation = CurrentLocation,
        });
        _priorityPriorityLocations.AddRange(
            serverSavedState.PriorityPriorityLocations.Select(l => GameDefinitions.Instance.LocationsByName[l])
        );
        _priorityLocations.AddRange(
            serverSavedState.PriorityLocations.Select(l => GameDefinitions.Instance.LocationsByName[l])
        );
        _initializedServerSavedState = true;
    }

    public void InitializeVictoryLocation(LocationKey victoryLocation)
    {
        using Lock.Scope __ = EnterLockScope();
        if (_victoryLocation != LocationKey.Nonexistent)
        {
            throw new InvalidOperationException("Victory location has already been initialized.");
        }

        ReadOnlySpan<LocationKey> allowedLocations =
        [
            GameDefinitions.Instance[GameDefinitions.Instance.RegionsByYamlKey["captured_goldfish"]].Locations[0],
            GameDefinitions.Instance[GameDefinitions.Instance.RegionsByYamlKey["secret_cache"]].Locations[0],
            GameDefinitions.Instance[GameDefinitions.Instance.RegionsByYamlKey["snakes_on_a_planet"]].Locations[0],
        ];

        if (!allowedLocations.Contains(victoryLocation))
        {
            throw new InvalidDataException("Location is not allowed to be a victory location.");
        }

        _victoryLocation = victoryLocation;
    }

    public AddPriorityLocationResult? AddPriorityLocation(LocationKey toPrioritize)
    {
        using Lock.Scope _ = EnterLockScope();
        EnsureStarted();
        if (_priorityLocations.Contains(toPrioritize))
        {
            return AddPriorityLocationResult.AlreadyPrioritized;
        }

        _priorityLocations.Add(toPrioritize);
        return _hardLockedRegions[GameDefinitions.Instance.Region[toPrioritize].N]
            ? AddPriorityLocationResult.AddedUnreachable
            : AddPriorityLocationResult.AddedReachable;
    }

    public LocationKey? RemovePriorityLocation(string locationName)
    {
        using Lock.Scope _ = EnterLockScope();
        EnsureStarted();
        int index = _priorityLocations.FindIndex(
            l => GameDefinitions.Instance[l].Name.Equals(locationName, StringComparison.InvariantCultureIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        LocationKey removed = _priorityLocations[index];
        _priorityLocations.RemoveAt(index);
        return removed;
    }

    public List<LocationKey> CalculateRoute(LocationKey fromLocation, LocationKey toLocation)
    {
        using Lock.Scope _ = EnterLockScope();
        EnsureStarted();
        GetPath(fromLocation, toLocation);
        return _prevPath;
    }

    public void EnsureStarted()
    {
        if (HasStarted)
        {
            return;
        }

        using Lock.Scope _ = EnterLockScope();
        if (HasStarted)
        {
            return;
        }

        _checkedLocationsInitialized = true;
        _receivedItemsInitialized = true;
        _ratCount = null;
        _spoilerData ??= GameDefinitions.Instance.UnrandomizedSpoilerData;
        _initializedServerSavedState = true;
        if (_victoryLocation == LocationKey.Nonexistent)
        {
            _victoryLocation = GameDefinitions.Instance[GameDefinitions.Instance.RegionsByYamlKey["snakes_on_a_planet"]].Locations[0];
        }

        _locationIsRelevant = GameDefinitions.Instance.GetLocationsBeforeVictoryLandmark(GameDefinitions.Instance.Region[_victoryLocation]);
        HasStarted = true;
        RecalculateClearable();
    }

    public void Advance()
    {
        EnsureStarted();
        using Lock.Scope _ = EnterLockScope();
        if (IsCompleted)
        {
            if (CurrentLocation != _victoryLocation)
            {
                CurrentLocation = _victoryLocation;
                TargetLocation = _victoryLocation;
                _prevMovementLog.Clear();
                _prevMovementLog.Add(new()
                {
                    PreviousLocation = _victoryLocation,
                    CurrentLocation = _victoryLocation,
                });
            }

            return;
        }

        _movementLog.Clear();
        bool bumpMercyModifierForNextTime = false;
        bool isFirstCheck = true;
        bool confirmedTargetLocation = false;
        int actionBalance = DefaultActionsPerStep + _actionBalanceAfterPreviousStep;
        switch (FoodFactor)
        {
            case < 0:
                --actionBalance;
                FoodFactor += 1;
                break;

            case > 0:
                ++actionBalance;
                FoodFactor -= 1;
                break;
        }

        // positive EnergyFactor lets the player make up to 2x as much distance in a single round of
        // (only) movement. in the past, this was uncapped, which basically meant that the player
        // would often teleport great distances, which was against the spirit of the whole thing.
        int energyBank = actionBalance;

        if (DistractionCounter > 0)
        {
            // being startled takes priority over a distraction. you just saw a ghost, you're not
            // thinking about the Rubik's Cube that you got at about the same time!
            if (StartledCounter == 0)
            {
                actionBalance = 0;
            }

            DistractionCounter -= 1;
        }

        int multi = 0;
        while (actionBalance > 0 && !IsCompleted)
        {
            --actionBalance;

            // changing your route takes an action unless you're startled.
            if (!confirmedTargetLocation && UpdateTargetLocation() && TargetLocationReason != TargetLocationReason.Startled)
            {
                confirmedTargetLocation = true;
                continue;
            }

            confirmedTargetLocation = true;
            bool moved = false;
            if (CurrentLocation != TargetLocation)
            {
                switch (EnergyFactor)
                {
                    case < 0:
                        --actionBalance;
                        EnergyFactor += 1;
                        break;

                    case > 0 when energyBank > 0:
                        ++actionBalance;
                        --energyBank;
                        EnergyFactor -= 1;
                        break;
                }

                // we're not in the right spot, so we're going to move at least a bit. playtesting
                // has shown that very long moves can be very boring (and a little too frequent). to
                // combat this, every time the player decides to move, they can advance up to three
                // whole spaces towards their target. this keeps the overall progression speed the
                // same in dense areas.
                for (int i = 0; i < MaxMovementsPerAction && CurrentLocation != TargetLocation && _pathToTarget.TryDequeue(out LocationKey dequeued); i++)
                {
                    _movementLog.Add(new()
                    {
                        PreviousLocation = CurrentLocation,
                        CurrentLocation = dequeued,
                    });

                    CurrentLocation = _movementLog[^1].CurrentLocation;
                    moved = true;
                }
            }

            if (!moved && StartledCounter == 0 && !_checkedLocations[CurrentLocation.N])
            {
                bool hasUnlucky = false;
                bool hasLucky = false;
                bool hasStylish = false;
                switch (LuckFactor)
                {
                    case < 0:
                        LuckFactor += 1;
                        hasUnlucky = true;
                        break;

                    case > 0:
                        LuckFactor -= 1;
                        hasLucky = true;
                        break;
                }

                if (StyleFactor > 0 && !hasLucky)
                {
                    StyleFactor -= 1;
                    hasStylish = true;
                }

                byte roll = 0;
                bool success = hasLucky;
                int immediateMercyModifier = MercyModifier;
                if (!success)
                {
                    sbyte modifiedRoll = ModifyRoll(
                        d20: roll = Prng.NextD20(ref _prngState),
                        mercy: immediateMercyModifier,
                        multi: multi++,
                        hasUnlucky: hasUnlucky,
                        hasStylish: hasStylish);
                    success = modifiedRoll >= GameDefinitions.Instance[CurrentLocation].AbilityCheckDC;
                    if (isFirstCheck && !success)
                    {
                        bumpMercyModifierForNextTime = true;
                    }

                    isFirstCheck = false;
                }

                if (success)
                {
                    MarkCurrentLocationChecked();
                }

                _instrumentation?.TraceLocationAttempt(CurrentLocation, roll, hasLucky, hasUnlucky, hasStylish, (byte)RatCount, (byte)GameDefinitions.Instance[CurrentLocation].AbilityCheckDC, (byte)immediateMercyModifier, success);
                if (!success)
                {
                    continue;
                }
            }

            if (CurrentLocation == TargetLocation)
            {
                switch (TargetLocationReason)
                {
                    case TargetLocationReason.Priority:
                        _priorityLocations.Remove(TargetLocation);
                        break;

                    case TargetLocationReason.PriorityPriority:
                        _priorityPriorityLocations.Remove(TargetLocation);
                        break;

                    case TargetLocationReason.GoMode when CurrentLocation == _victoryLocation:
                        MarkCurrentLocationChecked();
                        break;
                }
            }

            // don't burn more than one action per round on changing the target location. we only do
            // it at all because it represents the rat having to take time to "think" after a change
            // to its priorities or available actions.
            UpdateTargetLocation();
        }

        if (actionBalance > 0)
        {
            // it's possible to have a negative action counter due to a negative state.EnergyFactor,
            // and so we smear that move action across two rounds. but otherwise, this is very much
            // a "use it or lose it" system.
            actionBalance = 0;
        }

        if (StartledCounter > 0)
        {
            StartledCounter -= 1;
            UpdateTargetLocation();
        }

        if (_movementLog.Count == 0)
        {
            if (_prevMovementLog.Count > 1)
            {
                _prevMovementLog.RemoveRange(0, _prevMovementLog.Count - 1);
            }
        }
        else
        {
            _prevMovementLog.Clear();
            _prevMovementLog.AddRange(_movementLog);
        }

        _actionBalanceAfterPreviousStep = actionBalance;
        if (bumpMercyModifierForNextTime)
        {
            ++MercyModifier;
        }

        void MarkCurrentLocationChecked()
        {
            _checkedLocations[CurrentLocation.N] = true;
            _checkedLocationsOrder.Add(CurrentLocation);
            --_regionUncheckedLocationsCount[GameDefinitions.Instance.Region[CurrentLocation].N];
            _softLockedRegions[GameDefinitions.Instance.Region[CurrentLocation].N] = false;
            MercyModifier = 0;
            bumpMercyModifierForNextTime = false;
        }
    }

    public void CheckLocations(ReadOnlySpan<LocationKey> newLocations)
    {
        using Lock.Scope _ = EnterLockScope();
        EnsureStarted();
        BitArray384 locationIsNewlyChecked = new(GameDefinitions.Instance.AllLocations.Length);
        foreach (LocationKey location in newLocations)
        {
            if (_checkedLocations[location.N])
            {
                continue;
            }

            _checkedLocations[location.N] = true;
            _checkedLocationsOrder.Add(location);
            locationIsNewlyChecked[location.N] = true;
            RegionKey region = GameDefinitions.Instance.Region[location];
            _softLockedRegions[region.N] = false;
            --_regionUncheckedLocationsCount[region.N];
        }

        _priorityLocations.RemoveAll(l => locationIsNewlyChecked[l.N]);
        _priorityPriorityLocations.RemoveAll(l => locationIsNewlyChecked[l.N]);
    }

    public void ReceiveItems(ReadOnlySpan<ItemKey> newItems)
    {
        if (newItems.IsEmpty)
        {
            return;
        }

        using Lock.Scope _ = EnterLockScope();
        EnsureStarted();
        Span<int> receivedItems = _receivedItems;
        _receivedItemsOrder.EnsureCapacity(_receivedItemsOrder.Count + newItems.Length);
        bool recalculateAccess = false;
        int foodMod = 0;
        int energyFactorMod = 0;
        int luckFactorMod = 0;
        int distractedMod = 0;
        int stylishMod = 0;
        int startledMod = 0;
        foreach (ItemKey newItem in newItems)
        {
            _receivedItemsOrder.Add(newItem);
            ++receivedItems[newItem.N];
            ref readonly ItemDefinitionModel item = ref GameDefinitions.Instance[newItem];
            recalculateAccess |= item.ArchipelagoFlags == ArchipelagoItemFlags.LogicalAdvancement || item.RatCount > 0;

            // "confidence" takes place right away: it could apply to another item in the batch.
            bool addConfidence = false;
            bool subtractConfidence = false;
            foreach (string aura in item.AurasGranted)
            {
                switch (aura)
                {
                    case "upset_tummy" when HasConfidence:
                    case "unlucky" when HasConfidence:
                    case "sluggish" when HasConfidence:
                    case "distracted" when HasConfidence:
                    case "startled" when HasConfidence:
                    case "conspiratorial" when HasConfidence:
                        subtractConfidence = true;
                        break;

                    case "well_fed":
                        ++foodMod;
                        break;

                    case "upset_tummy":
                        --foodMod;
                        break;

                    case "lucky":
                        ++luckFactorMod;
                        break;

                    case "unlucky":
                        --luckFactorMod;
                        break;

                    case "energized":
                        ++energyFactorMod;
                        break;

                    case "sluggish":
                        --energyFactorMod;
                        break;

                    case "distracted":
                        ++distractedMod;
                        break;

                    case "stylish":
                        ++stylishMod;
                        break;

                    case "startled":
                        ++startledMod;
                        break;

                    case "smart":
                    case "conspiratorial":
                        if (recalculateAccess)
                        {
                            RecalculateClearable();
                            recalculateAccess = false;
                        }

                        AddPriorityPriorityLocationFor(aura == "smart" ? ArchipelagoItemFlags.LogicalAdvancement : ArchipelagoItemFlags.Trap);
                        break;

                    case "confident":
                        addConfidence = true;
                        break;
                }
            }

            // subtract first
            if (subtractConfidence)
            {
                HasConfidence = false;
            }

            if (addConfidence)
            {
                HasConfidence = true;
            }
        }

        _ratCount = null;
        FoodFactor += foodMod * 5;
        EnergyFactor += energyFactorMod * 5;
        LuckFactor += luckFactorMod;
        StyleFactor += stylishMod * 2;
        DistractionCounter += distractedMod;
        StartledCounter += startledMod;

        // Startled is extremely punishing. after a big release, it can be very annoying to just sit
        // there and wait for too many turns in a row. same concept applies to Distracted.
        StartledCounter = Math.Min(StartledCounter, 3);
        DistractionCounter = Math.Min(DistractionCounter, 3);

        if (recalculateAccess)
        {
            RecalculateClearable();
        }
    }

    private Lock.Scope EnterLockScope()
    {
        return _lock is null ? default : _lock.EnterScope();
    }
}
