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

public sealed record AuraData
{
    public int FoodFactor { get; init; }

    public int LuckFactor { get; init; }

    public int EnergyFactor { get; init; }

    public int StyleFactor { get; init; }

    public int DistractionCounter { get; init; }

    public int StartledCounter { get; init; }

    public bool HasConfidence { get; init; }

    public int MercyModifier { get; init; }

    public ImmutableArray<string> PriorityPriorityLocations { get; init; } = [];

    public ImmutableArray<string> PriorityLocations { get; init; } = [];
}

public sealed record LocationVector
{
    public required LocationDefinitionModel PreviousLocation { get; init; }

    public required LocationDefinitionModel CurrentLocation { get; init; }
}

public sealed class Game
{
    private readonly Lock _lock = new();

    private readonly GameInstrumentation? _instrumentation;

    private int _actionBalanceAfterPreviousStep;

    private bool _initializedAuraData;

    private RouteCalculator? _routeCalculator;

    private IEnumerator<LocationDefinitionModel>? _targetLocationPathEnumerator;

    private FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>>? _spoilerData;

    public Game(Prng.State prngState)
        : this(prngState, null)
    {
    }

    public Game(Prng.State prngState, GameInstrumentation? instrumentation)
    {
        _prngState = prngState;
        _instrumentation = instrumentation;
    }

    public ImmutableArray<LocationVector> PreviousStepMovementLog { get; private set; } = [
        new()
        {
            PreviousLocation = GameDefinitions.Instance.StartLocation,
            CurrentLocation = GameDefinitions.Instance.StartLocation,
        },
    ];

    public LocationDefinitionModel CurrentLocation { get; private set; } = GameDefinitions.Instance.StartLocation;

    public LocationDefinitionModel TargetLocation { get; private set; } = GameDefinitions.Instance.StartLocation;

    public TargetLocationReason TargetLocationReason { get; private set; } = TargetLocationReason.GameNotStarted;

    private List<ItemDefinitionModel>? _receivedItems;

    public ReadOnlyCollection<ItemDefinitionModel> ReceivedItems
    {
        get
        {
            EnsureStarted();
            return _receivedItems!.AsReadOnly();
        }
    }

    private CheckedLocations? _checkedLocations;

    public CheckedLocations CheckedLocations
    {
        get
        {
            EnsureStarted();
            return _checkedLocations!;
        }
    }

    private List<LocationDefinitionModel> _priorityPriorityLocations = [];

    public ReadOnlyCollection<LocationDefinitionModel> PriorityPriorityLocations => _priorityPriorityLocations.AsReadOnly();

    private List<LocationDefinitionModel> _priorityLocations = [];

    public ReadOnlyCollection<LocationDefinitionModel> PriorityLocations
    {
        get => _priorityLocations.AsReadOnly();
        private set
        {
            _priorityLocations.Clear();
            _priorityLocations.AddRange(value);
        }
    }

    public FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>> SpoilerData
    {
        get
        {
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
            EnsureStarted();
            return _ratCount ??= _receivedItems!
                .DefaultIfEmpty()
                .Sum(i => i?.RatCount.GetValueOrDefault())
                .GetValueOrDefault();
        }
    }

    public AuraData AuraData
    {
        get
        {
            if (!_initializedAuraData)
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
                PriorityPriorityLocations = [.. _priorityPriorityLocations.Select(l => l.Name)],
                PriorityLocations = [.. _priorityLocations.Select(l => l.Name)],
            };
        }
    }

    private Prng.State _prngState;
    public Prng.State PrngState { get => _prngState; private set => _prngState = value; }

    public bool HasStarted { get; private set; }

    public bool IsCompleted => CurrentLocation == GameDefinitions.Instance.GoalLocation;

    public int PermanentRollModifier => GetPermanentRollModifier(RatCount);
    public static int GetPermanentRollModifier(int ratCount)
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
            PermanentRollModifier +
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

    public void InitializeCheckedLocations(IEnumerable<LocationDefinitionModel> checkedLocations)
    {
        using Lock.Scope _ = _lock.EnterScope();
        if (_checkedLocations is not null)
        {
            throw new InvalidOperationException("Checked locations have already been initialized.");
        }

        _checkedLocations = new();
        foreach (LocationDefinitionModel location in checkedLocations)
        {
            _checkedLocations.MarkChecked(location);
        }
    }

    public void InitializeReceivedItems(IEnumerable<ItemDefinitionModel> receivedItems)
    {
        using Lock.Scope _ = _lock.EnterScope();
        if (_receivedItems is not null)
        {
            throw new InvalidOperationException("Received items have already been initialized.");
        }

        _receivedItems = [.. receivedItems];
        _ratCount = null;
    }

    public void InitializeSpoilerData(FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>> spoilerData)
    {
        using Lock.Scope _ = _lock.EnterScope();
        if (_spoilerData is not null)
        {
            throw new InvalidOperationException("Spoiler data has already been initialized.");
        }

        _spoilerData = spoilerData;
    }

    public void InitializeAuraData(AuraData auraData)
    {
        using Lock.Scope _ = _lock.EnterScope();
        if (_initializedAuraData)
        {
            throw new InvalidOperationException("Aura data has already been initialized.");
        }

        FoodFactor = auraData.FoodFactor;
        LuckFactor = auraData.LuckFactor;
        EnergyFactor = auraData.EnergyFactor;
        StyleFactor = auraData.StyleFactor;
        DistractionCounter = auraData.DistractionCounter;
        StartledCounter = auraData.StartledCounter;
        MercyModifier = auraData.MercyModifier;
        HasConfidence = auraData.HasConfidence;
        _priorityPriorityLocations =
        [
            .. auraData.PriorityPriorityLocations.Select(l => GameDefinitions.Instance.LocationsByName[l]),
        ];
        _priorityLocations =
        [
            .. auraData.PriorityLocations.Select(l => GameDefinitions.Instance.LocationsByName[l]),
        ];

        _initializedAuraData = true;
    }

    public AddPriorityLocationResult? AddPriorityLocation(LocationDefinitionModel toPrioritize)
    {
        using Lock.Scope _ = _lock.EnterScope();
        if (_priorityLocations.Contains(toPrioritize))
        {
            return AddPriorityLocationResult.AlreadyPrioritized;
        }

        _priorityLocations.Add(toPrioritize);
        return _routeCalculator?.CanReach(toPrioritize) switch
        {
            false => AddPriorityLocationResult.AddedUnreachable,
            _ => AddPriorityLocationResult.AddedReachable,
        };
    }

    public LocationDefinitionModel? RemovePriorityLocation(string locationName)
    {
        using Lock.Scope _ = _lock.EnterScope();
        int index = _priorityLocations.FindIndex(
            l => l.Name.Equals(locationName, StringComparison.InvariantCultureIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        LocationDefinitionModel removed = _priorityLocations[index];
        _priorityLocations.RemoveAt(index);
        return removed;
    }

    public IEnumerable<LocationDefinitionModel> CalculateRoute(LocationDefinitionModel fromLocation, LocationDefinitionModel toLocation)
    {
        using Lock.Scope _ = _lock.EnterScope();
        EnsureStarted();
        return _routeCalculator!.GetPath(fromLocation, toLocation) ?? [];
    }

    public void EnsureStarted()
    {
        using Lock.Scope _ = _lock.EnterScope();
        if (HasStarted)
        {
            return;
        }

        _checkedLocations ??= new();
        _receivedItems ??= [];
        _ratCount = null;
        _spoilerData ??= GameDefinitions.Instance.LocationsByName.Values
            .Where(l => l.UnrandomizedItem is not null)
            .GroupBy(l => l.UnrandomizedItem!.ArchipelagoFlags, l => l.Key)
            .ToFrozenDictionary(grp => grp.Key, grp => grp.ToFrozenSet());
        _initializedAuraData = true;
        _routeCalculator = new(this);
        HasStarted = true;
    }

    public void Advance()
    {
        using Lock.Scope _ = _lock.EnterScope();
        EnsureStarted();
        if (IsCompleted)
        {
            return;
        }

        bool bumpMercyModifierForNextTime = false;
        bool isFirstCheck = true;
        int actionBalance = 3 + _actionBalanceAfterPreviousStep;
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
        List<LocationVector> movementLog = [];

        // "Startled" has its own separate code to figure out the route to take.
        using IEnumerator<LocationDefinitionModel>? startledPath = actionBalance > 0 && StartledCounter > 0
            ? _routeCalculator!.GetStartledPath(CurrentLocation).GetEnumerator()
            : null;
        while (actionBalance > 0 && !IsCompleted)
        {
            --actionBalance;

            // changing your route takes an action unless you're startled.
            if (UpdateTargetLocation() && TargetLocationReason != TargetLocationReason.Startled)
            {
                continue;
            }

            bool moved = false;
            if (CurrentLocation != TargetLocation)
            {
                switch (EnergyFactor)
                {
                    case < 0:
                        --actionBalance;
                        EnergyFactor += 1;
                        break;

                    case > 0:
                        ++actionBalance;
                        EnergyFactor -= 1;
                        break;
                }

                // we're not in the right spot, so we're going to move at least a bit. playtesting
                // has shown that very long moves can be very boring (and a little too frequent). to
                // combat this, every time the player decides to move, they can advance up to three
                // whole spaces towards their target. this keeps the overall progression speed the
                // same in dense areas.
                for (int i = 0; i < 3 && CurrentLocation != TargetLocation; i++)
                {
                    if (startledPath?.MoveNext() == true)
                    {
                        movementLog.Add(new()
                        {
                            PreviousLocation = CurrentLocation,
                            CurrentLocation = startledPath.Current,
                        });
                    }
                    else
                    {
                        _targetLocationPathEnumerator!.MoveNext();
                        movementLog.Add(new()
                        {
                            PreviousLocation = CurrentLocation,
                            CurrentLocation = _targetLocationPathEnumerator.Current,
                        });
                    }

                    CurrentLocation = movementLog[^1].CurrentLocation;
                    moved = true;

                    _instrumentation?.TraceMovement(movementLog[^1].PreviousLocation, movementLog[^1].CurrentLocation, TargetLocationReason);
                }
            }

            if (!moved && StartledCounter == 0 && !_checkedLocations![CurrentLocation.Key])
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
                sbyte modifiedRoll = 0;
                bool success = hasLucky;
                int immediateMercyModifier = MercyModifier;
                if (!success)
                {
                    modifiedRoll = ModifyRoll(
                        d20: roll = Prng.NextD20(ref _prngState),
                        mercy: immediateMercyModifier,
                        multi: multi++,
                        hasUnlucky: hasUnlucky,
                        hasStylish: hasStylish);
                    success = modifiedRoll >= CurrentLocation.AbilityCheckDC;
                    if (isFirstCheck && !success)
                    {
                        bumpMercyModifierForNextTime = true;
                    }

                    isFirstCheck = false;
                }

                if (success)
                {
                    _checkedLocations!.MarkChecked(CurrentLocation);
                    MercyModifier = 0;
                    bumpMercyModifierForNextTime = false;
                    success = true;
                }

                _instrumentation?.TraceLocationAttempt(CurrentLocation, roll, modifiedRoll, hasLucky, hasUnlucky, hasStylish, (byte)RatCount, (byte)CurrentLocation.AbilityCheckDC, (byte)immediateMercyModifier, success);
                if (!success)
                {
                    continue;
                }
            }

            if (CurrentLocation == TargetLocation)
            {
                LocationDefinitionModel targetLocation = TargetLocation;
                switch (TargetLocationReason)
                {
                    case TargetLocationReason.Priority:
                        _priorityLocations.Remove(targetLocation);
                        break;

                    case TargetLocationReason.PriorityPriority:
                        _priorityPriorityLocations.Remove(targetLocation);
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

        if (movementLog.Count == 0)
        {
            if (PreviousStepMovementLog.Length > 1)
            {
                PreviousStepMovementLog = [PreviousStepMovementLog[^1]];
            }
        }
        else
        {
            PreviousStepMovementLog = [.. movementLog];
        }

        _actionBalanceAfterPreviousStep = actionBalance;
        if (bumpMercyModifierForNextTime)
        {
            ++MercyModifier;
        }
    }

    public void CheckLocations(ImmutableArray<LocationDefinitionModel> newLocations)
    {
        using Lock.Scope _ = _lock.EnterScope();
        EnsureStarted();
        foreach (LocationDefinitionModel location in newLocations)
        {
            _checkedLocations!.MarkChecked(location);
        }
    }

    public void ReceiveItems(ReadOnlySpan<ItemDefinitionModel> newItems)
    {
        using Lock.Scope _ = _lock.EnterScope();
        EnsureStarted();
        if (newItems.IsEmpty)
        {
            return;
        }

        int foodMod = 0;
        int energyFactorMod = 0;
        int luckFactorMod = 0;
        int distractedMod = 0;
        int stylishMod = 0;
        int startledMod = 0;
        foreach (ItemDefinitionModel newItem in newItems)
        {
            // "confidence" takes place right away: it could apply to another item in the batch.
            bool addConfidence = false;
            bool subtractConfidence = false;
            foreach (string aura in newItem.AurasGranted)
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
                        ArchipelagoItemFlags flags = aura == "smart" ? ArchipelagoItemFlags.LogicalAdvancement : ArchipelagoItemFlags.Trap;
                        FrozenSet<LocationDefinitionModel> toSkip = [.. _priorityPriorityLocations];
                        if (_routeCalculator!.GetClosestLocationsWithItemFlags(CurrentLocation, flags).FirstOrDefault(l => !toSkip.Contains(l)) is { } loc)
                        {
                            _priorityPriorityLocations.Add(loc);
                        }

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

        _receivedItems!.AddRange(newItems);
        _ratCount = null;
        FoodFactor += foodMod * 5;
        EnergyFactor += energyFactorMod * 5;
        LuckFactor += luckFactorMod;
        StyleFactor += stylishMod * 2;
        DistractionCounter += distractedMod;
        StartledCounter += startledMod;
    }

    private bool UpdateTargetLocation()
    {
        LocationDefinitionModel prevTargetLocation = TargetLocation;
        TargetLocation = BestTargetLocation(out TargetLocationReason bestTargetLocationReason);
        TargetLocationReason = bestTargetLocationReason;
        _targetLocationPathEnumerator ??= _routeCalculator!.GetPath(CurrentLocation, TargetLocation)!.GetEnumerator();
        if (TargetLocation == prevTargetLocation)
        {
            return false;
        }

        using IEnumerator<LocationDefinitionModel> _ = _targetLocationPathEnumerator;
        _targetLocationPathEnumerator = _routeCalculator!.GetPath(CurrentLocation, TargetLocation)!.GetEnumerator();
        return true;
    }

    private LocationDefinitionModel BestTargetLocation(out TargetLocationReason reason)
    {
        if (StartledCounter > 0)
        {
            reason = TargetLocationReason.Startled;
            return GameDefinitions.Instance.StartLocation;
        }

        if (_routeCalculator!.CanReachGoal() && _routeCalculator!.GetPath(CurrentLocation, GameDefinitions.Instance.GoalLocation) is { } path0)
        {
            reason = TargetLocationReason.GoMode;
            return path0.Prepend(CurrentLocation).FirstOrDefault(p => p.Region is LandmarkRegionDefinitionModel && !CheckedLocations[p]) ?? GameDefinitions.Instance.GoalLocation;
        }

        foreach (LocationDefinitionModel priorityPriorityLocation in _priorityPriorityLocations)
        {
            if (_routeCalculator!.GetPath(CurrentLocation, priorityPriorityLocation) is not { } path)
            {
                continue;
            }

            reason = TargetLocationReason.PriorityPriority;
            return path.Prepend(CurrentLocation).FirstOrDefault(p => p.Region is LandmarkRegionDefinitionModel && !CheckedLocations[p]) ?? priorityPriorityLocation;
        }

        foreach (LocationDefinitionModel priorityLocation in _priorityLocations)
        {
            if (_routeCalculator!.GetPath(CurrentLocation, priorityLocation) is not { } path)
            {
                continue;
            }

            reason = TargetLocationReason.Priority;
            return path.Prepend(CurrentLocation).FirstOrDefault(p => p.Region is LandmarkRegionDefinitionModel && !CheckedLocations[p]) ?? priorityLocation;
        }

        if (_routeCalculator!.FindClosestUncheckedLocation(CurrentLocation) is { } closestReachableUnchecked)
        {
            reason = TargetLocationReason.ClosestReachableUnchecked;
            return closestReachableUnchecked;
        }

        reason = TargetLocationReason.NowhereUsefulToMove;
        return CurrentLocation;
    }
}
