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

    public GameState State => _state;

    public void ArbitrarilyModifyState(Func<GameState, GameState> modify, [CallerFilePath] string? testFilePath = null)
    {
        // this isn't bulletproof. I need only avoid accidents, not malice.
        AllowFromTestsOnly(testFilePath);
        _state = modify(_state);
    }

    public bool AddPriorityLocation(LocationDefinitionModel toPrioritize)
    {
        if (_state.PriorityLocations.Contains(toPrioritize))
        {
            return false;
        }

        _state = _state with { PriorityLocations = _state.PriorityLocations.Add(toPrioritize) };
        return true;
    }

    public LocationDefinitionModel? RemovePriorityLocation(string locationName)
    {
        int index = _state.PriorityLocations.FindIndex(
            l => l.Name.Equals(locationName, StringComparison.InvariantCultureIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        LocationDefinitionModel removed = _state.PriorityLocations[index];
        _state = _state with { PriorityLocations = _state.PriorityLocations.RemoveAt(index) };
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
        HashSet<LocationDefinitionModel> uncheckedLocationsToIgnore = [.. _state.PriorityPriorityLocations];

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
                    case "upset_tummy" when _state.HasConfidence:
                    case "unlucky" when _state.HasConfidence:
                    case "sluggish" when _state.HasConfidence:
                    case "distracted" when _state.HasConfidence:
                    case "startled" when _state.HasConfidence:
                    case "conspiratorial" when _state.HasConfidence:
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
            ReceivedItems = new() { InReceivedOrder = _state.ReceivedItems.InReceivedOrder.AddRange(newItems) },
            FoodFactor = _state.FoodFactor + (foodMod * 5),
            EnergyFactor = _state.EnergyFactor + (energyFactorMod * 5),
            LuckFactor = _state.LuckFactor + luckFactorMod,
            StyleFactor = _state.StyleFactor + (stylishMod * 2),
            DistractionCounter = _state.DistractionCounter + distractedMod,
            StartledCounter = _state.StartledCounter + startledMod,
            PriorityPriorityLocations = _state.PriorityPriorityLocations.AddRange(priorityPriorityLocations),
        };
    }

    public void Advance()
    {
        if (_state.IsCompleted)
        {
            return;
        }

        int actionBalance = 3 + _state.ActionBalanceAfterPreviousStep;
        switch (_state.FoodFactor)
        {
            case < 0:
                --actionBalance;
                _state = _state with { FoodFactor = _state.FoodFactor + 1 };
                break;

            case > 0:
                ++actionBalance;
                _state = _state with { FoodFactor = _state.FoodFactor - 1 };
                break;
        }

        if (_state.DistractionCounter > 0)
        {
            // being startled takes priority over a distraction. you just saw a ghost, you're not
            // thinking about the Rubik's Cube that you got at about the same time!
            if (_state.StartledCounter == 0)
            {
                actionBalance = 0;
            }

            _state = _state with { DistractionCounter = _state.DistractionCounter - 1 };
        }

        List<LocationVector> movementLog = [];
        TargetLocationReason bestTargetLocationReason = _state.TargetLocationReason;
        while (actionBalance > 0 && !_state.IsCompleted)
        {
            --actionBalance;

            LocationDefinitionModel bestTargetLocation = BestTargetLocation(out bestTargetLocationReason, out ShortestPaths.Path bestPathToTargetLocation);
            if (_state.TargetLocation != bestTargetLocation)
            {
                // changing your route takes an action...
                _state = _state with { TargetLocation = bestTargetLocation };

                // ...unless you're startled, in which case it was instinct.
                if (bestTargetLocationReason != TargetLocationReason.Startled)
                {
                    continue;
                }
            }

            bool moved = false;
            if (_state.CurrentLocation != bestTargetLocation)
            {
                switch (_state.EnergyFactor)
                {
                    case < 0:
                        --actionBalance;
                        _state = _state with { EnergyFactor = _state.EnergyFactor + 1 };
                        break;

                    case > 0:
                        ++actionBalance;
                        _state = _state with { EnergyFactor = _state.EnergyFactor - 1 };
                        break;
                }

                // we're not in the right spot, so we're going to move at least a bit. playtesting
                // has shown that very long moves can be very boring (and a little too frequent). to
                // combat this, every time the player decides to move, they can advance up to three
                // whole spaces towards their target. this keeps the overall progression speed the
                // same in dense areas.
                for (int i = 0; i < 3 && _state.CurrentLocation != _state.TargetLocation; i++)
                {
                    movementLog.Add(new()
                    {
                        PreviousLocation = bestPathToTargetLocation.Locations[i],
                        CurrentLocation = bestPathToTargetLocation.Locations[i + 1],
                    });
                    _state = _state with { CurrentLocation = movementLog[^1].CurrentLocation };
                    moved = true;
                }
            }

            if (!moved && _state.StartledCounter == 0 && !_state.CheckedLocations.Contains(_state.CurrentLocation))
            {
                bool success = _state.CurrentLocation.TryCheck(ref _state);
                _state = _state with { LocationCheckAttemptsThisStep = _state.LocationCheckAttemptsThisStep + 1 };
                if (!success)
                {
                    continue;
                }
            }

            if (bestTargetLocationReason == TargetLocationReason.Priority && _state.CurrentLocation == _state.TargetLocation)
            {
                // we've reached our next priority location. remove it from the queue.
                LocationDefinitionModel targetLocation = _state.TargetLocation;
                _state = _state with { PriorityLocations = _state.PriorityLocations.RemoveAll(l => l == targetLocation) };
            }

            // figure out if anything above changed our best target location. if not, then don't
            // update anything so that the Epoch will stay the same!
            bestTargetLocation = BestTargetLocation(out bestTargetLocationReason, out bestPathToTargetLocation);
            if (bestTargetLocation != _state.TargetLocation)
            {
                _state = _state with { TargetLocation = bestTargetLocation };
            }
        }

        if (actionBalance > 0)
        {
            // it's possible to have a negative action counter due to a negative state.EnergyFactor,
            // and so we smear that move action across two rounds. but otherwise, this is very much
            // a "use it or lose it" system.
            actionBalance = 0;
        }

        if (_state.LocationCheckAttemptsThisStep != 0)
        {
            _state = _state with { LocationCheckAttemptsThisStep = 0 };
        }

        if (_state.ActionBalanceAfterPreviousStep != actionBalance)
        {
            _state = _state with { ActionBalanceAfterPreviousStep = actionBalance };
        }

        if (_state.StartledCounter > 0)
        {
            _state = _state with { StartledCounter = _state.StartledCounter - 1 };
            if (_state.StartledCounter == 0)
            {
                LocationDefinitionModel bestTargetLocation = BestTargetLocation(out _, out _);
                if (bestTargetLocation != _state.TargetLocation)
                {
                    _state = _state with { TargetLocation = bestTargetLocation };
                }
            }
        }

        if (movementLog.Count == 0)
        {
            if (_state.PreviousStepMovementLog.Length > 1)
            {
                _state = _state with { PreviousStepMovementLog = [_state.PreviousStepMovementLog[^1]] };
            }
        }
        else
        {
            _state = _state with { PreviousStepMovementLog = [.. movementLog] };
        }

        if (_state.TargetLocationReason != bestTargetLocationReason)
        {
            _state = _state with { TargetLocationReason = bestTargetLocationReason };
        }
    }

    private LocationDefinitionModel BestTargetLocation(out TargetLocationReason reason, out ShortestPaths.Path bestPath)
    {
        if (_state.StartledCounter > 0)
        {
            reason = TargetLocationReason.Startled;
            bestPath = _state.CheckedLocations.ShortestPaths.GetPathOrNull(_state.CurrentLocation, GameDefinitions.Instance.StartLocation)!.Value;
            return GameDefinitions.Instance.StartLocation;
        }

        foreach (LocationDefinitionModel priorityPriorityLocation in _state.PriorityPriorityLocations)
        {
            if (_state.ReceivedItems.ShortestPaths.GetPathOrNull(_state.CurrentLocation, priorityPriorityLocation) is ShortestPaths.Path priorityPath)
            {
                reason = priorityPriorityLocation == GameDefinitions.Instance.GoalLocation
                    ? TargetLocationReason.GoMode
                    : TargetLocationReason.PriorityPriority;
                bestPath = _state.CheckedLocations.ShortestPaths.GetPathOrNull(_state.CurrentLocation, priorityPriorityLocation) ?? priorityPath;
                return
                    bestPath.Locations.FirstOrDefault(l => l.Region is LandmarkRegionDefinitionModel landmark && !_state.CheckedLocations.Contains(landmark)) ??
                    bestPath.Locations[^1];
            }
        }

        foreach (LocationDefinitionModel priorityLocation in _state.PriorityLocations)
        {
            if (_state.ReceivedItems.ShortestPaths.GetPathOrNull(_state.CurrentLocation, priorityLocation) is ShortestPaths.Path priorityPath)
            {
                reason = TargetLocationReason.Priority;
                bestPath = _state.CheckedLocations.ShortestPaths.GetPathOrNull(_state.CurrentLocation, priorityLocation) ?? priorityPath;
                return
                    bestPath.Locations.FirstOrDefault(l => l.Region is LandmarkRegionDefinitionModel landmark && !_state.CheckedLocations.Contains(landmark)) ??
                    bestPath.Locations[^1];
            }
        }

        if (!_state.CheckedLocations.Contains(_state.CurrentLocation))
        {
            reason = TargetLocationReason.ClosestReachable;
            bestPath = _state.ReceivedItems.ShortestPaths.GetPathOrNull(_state.CurrentLocation, _state.CurrentLocation)!.Value;
            return _state.CurrentLocation;
        }

        PriorityQueue<LocationDefinitionModel, int> q = new();
        q.Enqueue(_state.CurrentLocation, 0);
        HashSet<LocationDefinitionModel> settled = [];
        while (q.TryDequeue(out LocationDefinitionModel? nextLocation, out int distance))
        {
            if (!settled.Add(nextLocation))
            {
                continue;
            }

            if (!_state.CheckedLocations.Contains(nextLocation))
            {
                reason = TargetLocationReason.ClosestReachable;
                bestPath = _state.ReceivedItems.ShortestPaths.GetPathOrNull(_state.CurrentLocation, nextLocation)!.Value;
                return nextLocation;
            }

            foreach (LocationDefinitionModel connected in GameDefinitions.Instance.ConnectedLocations[nextLocation])
            {
                if (settled.Contains(connected))
                {
                    // already fully handled this one.
                    continue;
                }

                if (connected.Region is LandmarkRegionDefinitionModel landmark && !landmark.Requirement.Satisfied(_state.ReceivedItems))
                {
                    // can't actually reach it to check it.
                    continue;
                }

                q.Enqueue(connected, distance + 1);
            }
        }

        reason = TargetLocationReason.NowhereUsefulToMove;
        bestPath = _state.ReceivedItems.ShortestPaths.GetPathOrNull(_state.CurrentLocation, _state.CurrentLocation)!.Value;
        return _state.CurrentLocation;
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
