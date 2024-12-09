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

public readonly record struct LocationVector
{
    public required LocationDefinitionModel PreviousLocation { get; init; }

    public required LocationDefinitionModel CurrentLocation { get; init; }
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

    private readonly List<LocationVector> _prevMovementLog = new(MaxMovementsPerStep);

    private readonly List<LocationVector> _movementLog = new(MaxMovementsPerStep);

    private readonly Queue<LocationDefinitionModel> _pathToTarget = new(GameDefinitions.Instance.LocationsByName.Count);

    private int _actionBalanceAfterPreviousStep;

    private bool _initializedAuraData;

    private FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>>? _spoilerData;

    public Game(Prng.State prngState)
        : this(prngState, null)
    {
    }

    public Game(Prng.State prngState, GameInstrumentation? instrumentation)
    {
        _hardLockedRegions.Remove(GameDefinitions.Instance.StartRegion.Key);
        _softLockedRegions.Remove(GameDefinitions.Instance.StartRegion.Key);

        _prngState = prngState;
        _instrumentation = instrumentation;
        _lock = instrumentation is null ? new() : null;
        _prevMovementLog.Add(new()
        {
            PreviousLocation = GameDefinitions.Instance.StartLocation,
            CurrentLocation = GameDefinitions.Instance.StartLocation,
        });
        PreviousStepMovementLog = _prevMovementLog.AsReadOnly();
    }

    public ReadOnlyCollection<LocationVector> PreviousStepMovementLog { get; }

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

    private List<LocationKey> _priorityPriorityLocations = [];

    public ReadOnlyCollection<LocationKey> PriorityPriorityLocations => _priorityPriorityLocations.AsReadOnly();

    private List<LocationKey> _priorityLocations = [];

    public ReadOnlyCollection<LocationKey> PriorityLocations
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
                foreach (ItemDefinitionModel item in _receivedItems!)
                {
                    _ratCount += item.RatCount.GetValueOrDefault();
                }
            }

            return _ratCount.Value;
        }
    }

    public AuraData AuraData
    {
        get
        {
            using Lock.Scope _ = EnterLockScope();
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
                PriorityPriorityLocations = [.. _priorityPriorityLocations.Select(l => GameDefinitions.Instance.LocationsByKey[l].Name)],
                PriorityLocations = [.. _priorityLocations.Select(l => GameDefinitions.Instance.LocationsByKey[l].Name)],
            };
        }
    }

    private Prng.State _prngState;
    public Prng.State PrngState { get => _prngState; set => _prngState = value; }

    public bool HasStarted { get; private set; }

    public bool IsCompleted => CurrentLocation == GameDefinitions.Instance.GoalLocation;

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

    public void InitializeCheckedLocations(IEnumerable<LocationDefinitionModel> checkedLocations)
    {
        using Lock.Scope _ = EnterLockScope();
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
        using Lock.Scope _ = EnterLockScope();
        if (_receivedItems is not null)
        {
            throw new InvalidOperationException("Received items have already been initialized.");
        }

        _receivedItems = [.. receivedItems];
        _ratCount = null;
    }

    public void InitializeSpoilerData(FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>> spoilerData)
    {
        using Lock.Scope _ = EnterLockScope();
        if (_spoilerData is not null)
        {
            throw new InvalidOperationException("Spoiler data has already been initialized.");
        }

        _spoilerData = spoilerData;
    }

    public void InitializeAuraData(AuraData auraData)
    {
        using Lock.Scope _ = EnterLockScope();
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
            .. auraData.PriorityPriorityLocations.Select(l => GameDefinitions.Instance.LocationsByName[l].Key),
        ];
        _priorityLocations =
        [
            .. auraData.PriorityLocations.Select(l => GameDefinitions.Instance.LocationsByName[l].Key),
        ];

        _initializedAuraData = true;
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
        return _hardLockedRegions.Contains(toPrioritize.RegionKey)
            ? AddPriorityLocationResult.AddedUnreachable
            : AddPriorityLocationResult.AddedReachable;
    }

    public LocationKey? RemovePriorityLocation(string locationName)
    {
        using Lock.Scope _ = EnterLockScope();
        EnsureStarted();
        int index = _priorityLocations.FindIndex(
            l => GameDefinitions.Instance.LocationsByKey[l].Name.Equals(locationName, StringComparison.InvariantCultureIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        LocationKey removed = _priorityLocations[index];
        _priorityLocations.RemoveAt(index);
        return removed;
    }

    public List<LocationDefinitionModel> CalculateRoute(LocationDefinitionModel fromLocation, LocationDefinitionModel toLocation)
    {
        using Lock.Scope _ = EnterLockScope();
        EnsureStarted();
        return GetPath(fromLocation, toLocation);
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

        _checkedLocations ??= new();
        _receivedItems ??= new(GameDefinitions.Instance.LocationsByName.Count);
        _ratCount = null;
        _spoilerData ??= GameDefinitions.Instance.LocationsByName.Values
            .Where(l => l.UnrandomizedItem is not null)
            .GroupBy(l => l.UnrandomizedItem!.ArchipelagoFlags, l => l.Key)
            .ToFrozenDictionary(grp => grp.Key, grp => grp.ToFrozenSet());
        _initializedAuraData = true;
        HasStarted = true;
        RecalculateClearable();
    }

    public void Advance()
    {
        EnsureStarted();
        if (IsCompleted)
        {
            return;
        }

        using Lock.Scope _ = EnterLockScope();
        if (IsCompleted)
        {
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
                for (int i = 0; i < MaxMovementsPerAction && CurrentLocation != TargetLocation; i++)
                {
                    _movementLog.Add(new()
                    {
                        PreviousLocation = CurrentLocation,
                        CurrentLocation = _pathToTarget.Dequeue(),
                    });

                    CurrentLocation = _movementLog[^1].CurrentLocation;
                    moved = true;
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
                    _softLockedRegions.Remove(CurrentLocation.Key.RegionKey);
                    MercyModifier = 0;
                    bumpMercyModifierForNextTime = false;
                }

                _instrumentation?.TraceLocationAttempt(CurrentLocation, roll, hasLucky, hasUnlucky, hasStylish, (byte)RatCount, (byte)CurrentLocation.AbilityCheckDC, (byte)immediateMercyModifier, success);
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
                        _priorityLocations.Remove(TargetLocation.Key);
                        break;

                    case TargetLocationReason.PriorityPriority:
                        _priorityPriorityLocations.Remove(TargetLocation.Key);
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
    }

    public void CheckLocations(ReadOnlySpan<LocationDefinitionModel> newLocations)
    {
        using Lock.Scope _ = EnterLockScope();
        EnsureStarted();
        HashSet<LocationKey> keys = new(newLocations.Length);
        foreach (LocationDefinitionModel location in newLocations)
        {
            _checkedLocations!.MarkChecked(location);
            keys.Add(location.Key);
            _softLockedRegions.Remove(CurrentLocation.Key.RegionKey);
        }

        _priorityLocations.RemoveAll(keys.Contains);
        _priorityPriorityLocations.RemoveAll(keys.Contains);
    }

    public void ReceiveItems(ReadOnlySpan<ItemDefinitionModel> newItems)
    {
        if (newItems.IsEmpty)
        {
            return;
        }

        using Lock.Scope _ = EnterLockScope();
        EnsureStarted();
        bool recalculateAccess = false;
        int foodMod = 0;
        int energyFactorMod = 0;
        int luckFactorMod = 0;
        int distractedMod = 0;
        int stylishMod = 0;
        int startledMod = 0;
        foreach (ItemDefinitionModel newItem in newItems)
        {
            recalculateAccess |= GameDefinitions.Instance.ProgressionItemNames.Contains(newItem.Name) || newItem.RatCount > 0;

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
                        foreach (LocationKey loc in GetClosestLocationsWithItemFlags(CurrentLocation.Key, flags))
                        {
                            if (!_priorityPriorityLocations.Contains(loc))
                            {
                                _priorityPriorityLocations.Add(loc);
                                break;
                            }
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
