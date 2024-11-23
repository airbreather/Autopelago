using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Autopelago;

public sealed record LocationVector
{
    public required LocationDefinitionModel PreviousLocation { get; init; }

    public required LocationDefinitionModel CurrentLocation { get; init; }
}

public sealed partial class Game
{
    private readonly FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags> _spoilerData;

    private GameState _state;

    private ShortestPaths.Path _pathToTargetLocation = ShortestPaths.Path.Only(GameDefinitions.Instance.StartLocation);

    private int _locationCheckAttemptsThisStep;

    private int _actionBalanceAfterPreviousStep;

    public Game(GameState initialState, [CallerFilePath] string? testFilePath = null)
        : this(initialState, GameDefinitions.Instance.LocationsByName.Values.ToFrozenDictionary(l => l, l => l.UnrandomizedItem?.ArchipelagoFlags ?? ArchipelagoItemFlags.None))
    {
        // this isn't bulletproof. I need only avoid accidents, not malice.
        AllowFromTestsOnly(testFilePath);
    }

    public Game(GameState initialState, FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags> spoilerData)
    {
        _state = initialState;
        _spoilerData = spoilerData;
    }

    public ImmutableArray<LocationVector> PreviousStepMovementLog { get; private set; } = [];

    public LocationDefinitionModel CurrentLocation => _state.CurrentLocation;

    public LocationDefinitionModel TargetLocation => _state.TargetLocation;

    public TargetLocationReason TargetLocationReason { get; private set; } = TargetLocationReason.GameNotStarted;

    public ReceivedItems ReceivedItems => _state.ReceivedItems;

    public CheckedLocations CheckedLocations => _state.CheckedLocations;

    public ImmutableList<LocationDefinitionModel> PriorityPriorityLocations { get; private set; } = [GameDefinitions.Instance.GoalLocation];

    public ImmutableList<LocationDefinitionModel> PriorityLocations => _state.PriorityLocations;

    public int FoodFactor => _state.FoodFactor;

    public int LuckFactor => _state.LuckFactor;

    public int EnergyFactor => _state.EnergyFactor;

    public int StyleFactor => _state.StyleFactor;

    public int DistractionCounter => _state.DistractionCounter;

    public int StartledCounter => _state.StartledCounter;

    public bool HasConfidence => _state.HasConfidence;

    public Prng.State PrngState => _state.PrngState;

    public bool IsCompleted => CurrentLocation == GameDefinitions.Instance.GoalLocation;

    private int DiceModifier => (ReceivedItems.RatCount / 3) - (_locationCheckAttemptsThisStep * 5);

    public void ArbitrarilyModifyState(Func<GameState, GameState> modify, [CallerFilePath] string? testFilePath = null)
    {
        // this isn't bulletproof. I need only avoid accidents, not malice.
        AllowFromTestsOnly(testFilePath);
        _state = modify(_state);
    }

    public bool AddPriorityLocation(LocationDefinitionModel toPrioritize)
    {
        if (PriorityLocations.Contains(toPrioritize))
        {
            return false;
        }

        _state = _state with { PriorityLocations = PriorityLocations.Add(toPrioritize) };
        return true;
    }

    public LocationDefinitionModel? RemovePriorityLocation(string locationName)
    {
        int index = PriorityLocations.FindIndex(
            l => l.Name.Equals(locationName, StringComparison.InvariantCultureIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        LocationDefinitionModel removed = PriorityLocations[index];
        _state = _state with { PriorityLocations = PriorityLocations.RemoveAt(index) };
        return removed;
    }

    public void ReceiveItems(ImmutableArray<ItemDefinitionModel> newItems)
    {
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
        List<LocationDefinitionModel> priorityPriorityLocations = [];
        HashSet<LocationDefinitionModel> uncheckedLocationsToIgnore = [.. PriorityPriorityLocations];

        bool VisitLocation(LocationDefinitionModel curr, ArchipelagoItemFlags flags)
        {
            if (uncheckedLocationsToIgnore.Contains(curr) || _spoilerData[curr] != flags)
            {
                return true;
            }

            priorityPriorityLocations.Add(curr);
            uncheckedLocationsToIgnore.Add(curr);
            return false;
        }

        LocationVisitor smartVisitor = LocationVisitor.Create((curr, _, _, alreadyChecked) =>
            alreadyChecked || VisitLocation(curr, ArchipelagoItemFlags.LogicalAdvancement));
        LocationVisitor conspiratorialVisitor = LocationVisitor.Create((curr, _, _, alreadyChecked) =>
            alreadyChecked || VisitLocation(curr, ArchipelagoItemFlags.Trap));

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
                        VisitLocationsByDistanceFromCurrentLocation(smartVisitor, UncheckedLandmarkBehavior.PassThroughIfRequirementsSatisfied);
                        break;

                    case "conspiratorial":
                        VisitLocationsByDistanceFromCurrentLocation(conspiratorialVisitor, UncheckedLandmarkBehavior.PassThroughIfRequirementsSatisfied);
                        break;

                    case "confident":
                        addConfidence = true;
                        break;
                }
            }

            // subtract first
            if (subtractConfidence)
            {
                _state = _state with { HasConfidence = false };
            }

            if (addConfidence)
            {
                _state = _state with { HasConfidence = true };
            }
        }

        _state = _state with
        {
            ReceivedItems = new() { InReceivedOrder = ReceivedItems.InReceivedOrder.AddRange(newItems) },
            FoodFactor = FoodFactor + (foodMod * 5),
            EnergyFactor = EnergyFactor + (energyFactorMod * 5),
            LuckFactor = LuckFactor + luckFactorMod,
            StyleFactor = StyleFactor + (stylishMod * 2),
            DistractionCounter = DistractionCounter + distractedMod,
            StartledCounter = StartledCounter + startledMod,
        };
        PriorityPriorityLocations = PriorityPriorityLocations.AddRange(priorityPriorityLocations);
    }

    public void Advance()
    {
        if (IsCompleted)
        {
            return;
        }

        int actionBalance = 3 + _actionBalanceAfterPreviousStep;
        switch (FoodFactor)
        {
            case < 0:
                --actionBalance;
                _state = _state with { FoodFactor = FoodFactor + 1 };
                break;

            case > 0:
                ++actionBalance;
                _state = _state with { FoodFactor = FoodFactor - 1 };
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

            _state = _state with { DistractionCounter = DistractionCounter - 1 };
        }

        List<LocationVector> movementLog = [];
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
                        _state = _state with { EnergyFactor = EnergyFactor + 1 };
                        break;

                    case > 0:
                        ++actionBalance;
                        _state = _state with { EnergyFactor = EnergyFactor - 1 };
                        break;
                }

                // we're not in the right spot, so we're going to move at least a bit. playtesting
                // has shown that very long moves can be very boring (and a little too frequent). to
                // combat this, every time the player decides to move, they can advance up to three
                // whole spaces towards their target. this keeps the overall progression speed the
                // same in dense areas.
                for (int i = 0; i < 3 && CurrentLocation != TargetLocation; i++)
                {
                    movementLog.Add(new()
                    {
                        PreviousLocation = _pathToTargetLocation.Locations[i],
                        CurrentLocation = _pathToTargetLocation.Locations[i + 1],
                    });
                    _state = _state with { CurrentLocation = movementLog[^1].CurrentLocation };
                    moved = true;
                }
            }

            if (!moved && StartledCounter == 0 && !CheckedLocations.Contains(CurrentLocation))
            {
                bool success = TryCheck(CurrentLocation);
                _locationCheckAttemptsThisStep += 1;
                if (!success)
                {
                    continue;
                }
            }

            if (CurrentLocation == TargetLocation)
            {
                LocationDefinitionModel targetLocation = TargetLocation;
                _state = TargetLocationReason switch
                {
                    TargetLocationReason.Priority => _state with { PriorityLocations = PriorityLocations.RemoveAll(l => l == targetLocation) },
                    _ => _state,
                };
                PriorityPriorityLocations = PriorityPriorityLocations.RemoveAll(l => l == targetLocation);
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
            _state = _state with { StartledCounter = StartledCounter - 1 };
            UpdateTargetLocation();
        }

        if (movementLog.Count == 0 && PreviousStepMovementLog.Length > 1)
        {
            PreviousStepMovementLog = [PreviousStepMovementLog[^1]];
        }
        else
        {
            PreviousStepMovementLog = [.. movementLog];
        }

        _actionBalanceAfterPreviousStep = actionBalance;
        _locationCheckAttemptsThisStep = 0;
    }

    private bool UpdateTargetLocation()
    {
        LocationDefinitionModel prevTargetLocation = TargetLocation;
        _state = _state with
        {
            TargetLocation = BestTargetLocation(out TargetLocationReason bestTargetLocationReason, out ShortestPaths.Path bestPathToTargetLocation),
        };
        TargetLocationReason = bestTargetLocationReason;
        _pathToTargetLocation = bestPathToTargetLocation;
        return TargetLocation != prevTargetLocation;
    }

    private LocationDefinitionModel BestTargetLocation(out TargetLocationReason reason, out ShortestPaths.Path bestPath)
    {
        if (StartledCounter > 0)
        {
            reason = TargetLocationReason.Startled;
            bestPath = CheckedLocations.ShortestPaths.GetPathOrNull(CurrentLocation, GameDefinitions.Instance.StartLocation)!.Value;
            return GameDefinitions.Instance.StartLocation;
        }

        foreach (LocationDefinitionModel priorityPriorityLocation in PriorityPriorityLocations)
        {
            if (ReceivedItems.ShortestPaths.GetPathOrNull(CurrentLocation, priorityPriorityLocation) is ShortestPaths.Path priorityPath)
            {
                reason = priorityPriorityLocation == GameDefinitions.Instance.GoalLocation
                    ? TargetLocationReason.GoMode
                    : TargetLocationReason.PriorityPriority;
                bestPath = CheckedLocations.ShortestPaths.GetPathOrNull(CurrentLocation, priorityPriorityLocation) ?? priorityPath;
                return
                    bestPath.Locations.FirstOrDefault(l => l.Region is LandmarkRegionDefinitionModel landmark && !CheckedLocations.Contains(landmark)) ??
                    bestPath.Locations[^1];
            }
        }

        foreach (LocationDefinitionModel priorityLocation in PriorityLocations)
        {
            if (ReceivedItems.ShortestPaths.GetPathOrNull(CurrentLocation, priorityLocation) is ShortestPaths.Path priorityPath)
            {
                reason = TargetLocationReason.Priority;
                bestPath = CheckedLocations.ShortestPaths.GetPathOrNull(CurrentLocation, priorityLocation) ?? priorityPath;
                return
                    bestPath.Locations.FirstOrDefault(l => l.Region is LandmarkRegionDefinitionModel landmark && !CheckedLocations.Contains(landmark)) ??
                    bestPath.Locations[^1];
            }
        }

        if (!CheckedLocations.Contains(CurrentLocation))
        {
            reason = TargetLocationReason.ClosestReachable;
            bestPath = ReceivedItems.ShortestPaths.GetPathOrNull(CurrentLocation, CurrentLocation)!.Value;
            return CurrentLocation;
        }

        PriorityQueue<LocationDefinitionModel, int> q = new();
        q.Enqueue(CurrentLocation, 0);
        HashSet<LocationDefinitionModel> settled = [];
        while (q.TryDequeue(out LocationDefinitionModel? nextLocation, out int distance))
        {
            if (!settled.Add(nextLocation))
            {
                continue;
            }

            if (!CheckedLocations.Contains(nextLocation))
            {
                reason = TargetLocationReason.ClosestReachable;
                bestPath = ReceivedItems.ShortestPaths.GetPathOrNull(CurrentLocation, nextLocation)!.Value;
                return nextLocation;
            }

            foreach (LocationDefinitionModel connected in GameDefinitions.Instance.ConnectedLocations[nextLocation])
            {
                if (settled.Contains(connected))
                {
                    // already fully handled this one.
                    continue;
                }

                if (connected.Region is LandmarkRegionDefinitionModel landmark && !landmark.Requirement.Satisfied(ReceivedItems))
                {
                    // can't actually reach it to check it.
                    continue;
                }

                q.Enqueue(connected, distance + 1);
            }
        }

        reason = TargetLocationReason.NowhereUsefulToMove;
        bestPath = ReceivedItems.ShortestPaths.GetPathOrNull(CurrentLocation, CurrentLocation)!.Value;
        return CurrentLocation;
    }

    private bool TryCheck(LocationDefinitionModel location)
    {
        int extraDiceModifier = 0;
        switch (_state.LuckFactor)
        {
            case < 0:
                extraDiceModifier -= 5;
                _state = _state with { LuckFactor = _state.LuckFactor + 1 };
                break;

            case > 0:
                _state = _state with { LuckFactor = _state.LuckFactor - 1 };
                goto success;
        }

        if (_state.StyleFactor > 0)
        {
            extraDiceModifier += 5;
            _state = _state with { StyleFactor = _state.StyleFactor - 1 };
        }

        if (GameState.NextD20(ref _state) + DiceModifier + extraDiceModifier < location.AbilityCheckDC)
        {
            return false;
        }

        success:
        _state = _state with
        {
            CheckedLocations = new()
            {
                InCheckedOrder = _state.CheckedLocations.InCheckedOrder.Add(location),
            },
        };
        return true;
    }

    private void VisitLocationsByDistanceFromCurrentLocation(LocationVisitor visitor, UncheckedLandmarkBehavior uncheckedLandmarkBehavior)
    {
        IEnumerable<string> allowedRegions = GameDefinitions.Instance.FillerRegions.Keys;
        switch (uncheckedLandmarkBehavior)
        {
            case UncheckedLandmarkBehavior.PassThroughIfRequirementsSatisfied:
                allowedRegions = allowedRegions.Concat(GameDefinitions.Instance.LandmarkRegions.Values
                    .Where(r => r.Requirement.Satisfied(ReceivedItems))
                    .Select(r => r.Key));
                goto case UncheckedLandmarkBehavior.DoNotPassThrough;

            case UncheckedLandmarkBehavior.DoNotPassThrough:
                allowedRegions = allowedRegions.Concat(CheckedLocations.InCheckedOrder.Select(l => l.Key.RegionKey));
                break;

            case UncheckedLandmarkBehavior.AlwaysPassThrough:
                allowedRegions = GameDefinitions.Instance.AllRegions.Keys;
                break;
        }

        FrozenSet<string> allowedRegionsSet = [.. allowedRegions];

        // we skip visiting CurrentLocation so that previousLocation can be non-nullable
        HashSet<LocationDefinitionModel> visitedLocations = [CurrentLocation];
        PriorityQueue<(LocationDefinitionModel CurrentLocation, LocationDefinitionModel PreviousLocation), int> q = new();
        foreach (LocationDefinitionModel connectedLocation in GameDefinitions.Instance.ConnectedLocations[CurrentLocation])
        {
            if (allowedRegionsSet.Contains(connectedLocation.Key.RegionKey))
            {
                q.Enqueue((connectedLocation, CurrentLocation), 1);
            }
        }

        while (q.TryDequeue(out var tup, out int distance))
        {
            (LocationDefinitionModel curr, LocationDefinitionModel prev) = tup;
            if (!visitedLocations.Add(curr))
            {
                continue;
            }

            if (!visitor.VisitLocation(
                    currentLocation: curr,
                    previousLocation: prev,
                    distance: distance,
                    alreadyChecked: CheckedLocations.Contains(curr)))
            {
                return;
            }

            foreach (LocationDefinitionModel next in GameDefinitions.Instance.ConnectedLocations[curr])
            {
                // allow the goal location here: if it's connected to a reachable location, then we
                // consider it to be reachable itself. this lets us get away with not having to keep
                // track of the locations with fixed rewards, though a future YAML change could make
                // that no longer correct, so it feels A LITTLE BIT hacky.
                if (next == GameDefinitions.Instance.GoalLocation ||
                    allowedRegionsSet.Contains(next.Key.RegionKey) && !visitedLocations.Contains(next))
                {
                    q.Enqueue((next, curr), distance + 1);
                }
            }
        }
    }

    private static void AllowFromTestsOnly(ReadOnlySpan<char> filePath, [CallerArgumentExpression(nameof(filePath))] string? paramName = null)
    {
        if (!MyRegex().IsMatch(filePath))
        {
            throw new ArgumentException("Spoiler data is required for all real callers.", paramName);
        }
    }

    [GeneratedRegex(@"Autopelago\.Test[/\\][^/\\]*\.cs", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MyRegex();
}
