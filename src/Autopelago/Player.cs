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

public sealed partial class Player
{
    private readonly FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags> _spoilerData;

    private GameState _state;

    public Player(GameState initialState, [CallerFilePath] string? testFilePath = null)
        : this(initialState, GameDefinitions.Instance.LocationsByName.Values.ToFrozenDictionary(l => l, l => l.UnrandomizedItem?.ArchipelagoFlags ?? ArchipelagoItemFlags.None))
    {
        // this isn't bulletproof. I need only avoid accidents, not malice.
        AllowFromTestsOnly(testFilePath);
    }

    public Player(GameState initialState, FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags> spoilerData)
    {
        _state = initialState;
        _spoilerData = spoilerData;
    }

    public ImmutableArray<LocationVector> PreviousStepMovementLog => _state.PreviousStepMovementLog;

    public LocationDefinitionModel CurrentLocation => _state.CurrentLocation;

    public LocationDefinitionModel TargetLocation => _state.TargetLocation;

    public TargetLocationReason TargetLocationReason => _state.TargetLocationReason;

    private ShortestPaths.Path PathToTargetLocation => _state.PathToTargetLocation;

    public ReceivedItems ReceivedItems => _state.ReceivedItems;

    public CheckedLocations CheckedLocations => _state.CheckedLocations;

    public ImmutableList<LocationDefinitionModel> PriorityPriorityLocations => _state.PriorityPriorityLocations;

    public ImmutableList<LocationDefinitionModel> PriorityLocations => _state.PriorityLocations;

    public int FoodFactor => _state.FoodFactor;

    public int LuckFactor => _state.LuckFactor;

    public int EnergyFactor => _state.EnergyFactor;

    public int StyleFactor => _state.StyleFactor;

    public int DistractionCounter => _state.DistractionCounter;

    public int StartledCounter => _state.StartledCounter;

    public bool HasConfidence => _state.HasConfidence;

    private int LocationCheckAttemptsThisStep => _state.LocationCheckAttemptsThisStep;

    private int ActionBalanceAfterPreviousStep => _state.ActionBalanceAfterPreviousStep;

    public Prng.State PrngState => _state.PrngState;

    public bool IsCompleted => _state.IsCompleted;

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
                        _state.VisitLocationsByDistanceFromCurrentLocation(smartVisitor, UncheckedLandmarkBehavior.PassThroughIfRequirementsSatisfied);
                        break;

                    case "conspiratorial":
                        _state.VisitLocationsByDistanceFromCurrentLocation(conspiratorialVisitor, UncheckedLandmarkBehavior.PassThroughIfRequirementsSatisfied);
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
            PriorityPriorityLocations = PriorityPriorityLocations.AddRange(priorityPriorityLocations),
        };
    }

    public void Advance()
    {
        if (IsCompleted)
        {
            return;
        }

        int actionBalance = 3 + ActionBalanceAfterPreviousStep;
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
                        PreviousLocation = PathToTargetLocation.Locations[i],
                        CurrentLocation = PathToTargetLocation.Locations[i + 1],
                    });
                    _state = _state with { CurrentLocation = movementLog[^1].CurrentLocation };
                    moved = true;
                }
            }

            if (!moved && StartledCounter == 0 && !CheckedLocations.Contains(CurrentLocation))
            {
                bool success = TryCheck(CurrentLocation);
                _state = _state with { LocationCheckAttemptsThisStep = LocationCheckAttemptsThisStep + 1 };
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
                    TargetLocationReason.PriorityPriority => _state with { PriorityPriorityLocations = PriorityPriorityLocations.RemoveAll(l => l == targetLocation) },
                    TargetLocationReason.Priority => _state with { PriorityLocations = PriorityLocations.RemoveAll(l => l == targetLocation) },
                    _ => _state,
                };
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
            _state = _state with { PreviousStepMovementLog = [PreviousStepMovementLog[^1]] };
        }
        else
        {
            _state = _state with { PreviousStepMovementLog = [.. movementLog] };
        }

        _state = _state with
        {
            LocationCheckAttemptsThisStep = 0,
            ActionBalanceAfterPreviousStep = actionBalance,
        };
    }

    private bool UpdateTargetLocation()
    {
        LocationDefinitionModel prevTargetLocation = TargetLocation;
        _state = _state with
        {
            TargetLocation = BestTargetLocation(out TargetLocationReason bestTargetLocationReason, out ShortestPaths.Path bestPathToTargetLocation),
            TargetLocationReason = bestTargetLocationReason,
            PathToTargetLocation = bestPathToTargetLocation,
        };
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

        if (GameState.NextD20(ref _state) + _state.DiceModifier + extraDiceModifier < location.AbilityCheckDC)
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
