using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Autopelago;

public sealed record LocationVector
{
    public required LocationDefinitionModel PreviousLocation { get; init; }

    public required LocationDefinitionModel CurrentLocation { get; init; }
}

public sealed class Player
{
    public GameState ReceiveItems(GameState state, ImmutableArray<ItemDefinitionModel> newItems, FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags>? spoilerData = null)
    {
        if (newItems.IsEmpty)
        {
            return state;
        }

        spoilerData ??= GameDefinitions.Instance.LocationsByName.Values.ToFrozenDictionary(loc => loc, _ => ArchipelagoItemFlags.None);
        int foodMod = 0;
        int energyFactorMod = 0;
        int luckFactorMod = 0;
        int distractedMod = 0;
        int stylishMod = 0;
        int startledMod = 0;
        List<LocationDefinitionModel> priorityPriorityLocations = [];
        HashSet<LocationDefinitionModel> uncheckedLocationsToIgnore = [.. state.PriorityPriorityLocations];

        bool VisitLocation(LocationDefinitionModel curr, ArchipelagoItemFlags flags)
        {
            if (uncheckedLocationsToIgnore.Contains(curr) || spoilerData[curr] != flags)
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
                    case "upset_tummy" when state.HasConfidence:
                    case "unlucky" when state.HasConfidence:
                    case "sluggish" when state.HasConfidence:
                    case "distracted" when state.HasConfidence:
                    case "startled" when state.HasConfidence:
                    case "conspiratorial" when state.HasConfidence:
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
                        state.VisitLocationsByDistanceFromCurrentLocation(smartVisitor, UncheckedLandmarkBehavior.PassThroughIfRequirementsSatisfied);
                        break;

                    case "conspiratorial":
                        state.VisitLocationsByDistanceFromCurrentLocation(conspiratorialVisitor, UncheckedLandmarkBehavior.PassThroughIfRequirementsSatisfied);
                        break;

                    case "confident":
                        addConfidence = true;
                        break;
                }
            }

            // subtract first
            if (subtractConfidence)
            {
                state = state with { HasConfidence = false };
            }

            if (addConfidence)
            {
                state = state with { HasConfidence = true };
            }
        }

        return state with
        {
            ReceivedItems = new() { InReceivedOrder = state.ReceivedItems.InReceivedOrder.AddRange(newItems) },
            FoodFactor = state.FoodFactor + (foodMod * 5),
            EnergyFactor = state.EnergyFactor + (energyFactorMod * 5),
            LuckFactor = state.LuckFactor + luckFactorMod,
            StyleFactor = state.StyleFactor + (stylishMod * 2),
            DistractionCounter = state.DistractionCounter + distractedMod,
            StartledCounter = state.StartledCounter + startledMod,
            PriorityPriorityLocations = state.PriorityPriorityLocations.AddRange(priorityPriorityLocations),
        };
    }

    public GameState Advance(GameState state)
    {
        if (state.IsCompleted)
        {
            return state;
        }

        int actionBalance = 3 + state.ActionBalanceAfterPreviousStep;
        switch (state.FoodFactor)
        {
            case < 0:
                --actionBalance;
                state = state with { FoodFactor = state.FoodFactor + 1 };
                break;

            case > 0:
                ++actionBalance;
                state = state with { FoodFactor = state.FoodFactor - 1 };
                break;
        }

        if (state.DistractionCounter > 0)
        {
            // being startled takes priority over a distraction. you just saw a ghost, you're not
            // thinking about the Rubik's Cube that you got at about the same time!
            if (state.StartledCounter == 0)
            {
                actionBalance = 0;
            }

            state = state with { DistractionCounter = state.DistractionCounter - 1 };
        }

        List<LocationVector> movementLog = [];
        TargetLocationReason bestTargetLocationReason = state.TargetLocationReason;
        while (actionBalance > 0 && !state.IsCompleted)
        {
            --actionBalance;

            LocationDefinitionModel bestTargetLocation = BestTargetLocation(state, out bestTargetLocationReason, out ShortestPaths.Path bestPathToTargetLocation);
            if (state.TargetLocation != bestTargetLocation)
            {
                // changing your route takes an action...
                state = state with { TargetLocation = bestTargetLocation };

                // ...unless you're startled, in which case it was instinct.
                if (bestTargetLocationReason != TargetLocationReason.Startled)
                {
                    continue;
                }
            }

            bool moved = false;
            if (state.CurrentLocation != bestTargetLocation)
            {
                switch (state.EnergyFactor)
                {
                    case < 0:
                        --actionBalance;
                        state = state with { EnergyFactor = state.EnergyFactor + 1 };
                        break;

                    case > 0:
                        ++actionBalance;
                        state = state with { EnergyFactor = state.EnergyFactor - 1 };
                        break;
                }

                // we're not in the right spot, so we're going to move at least a bit. playtesting
                // has shown that very long moves can be very boring (and a little too frequent). to
                // combat this, every time the player decides to move, they can advance up to three
                // whole spaces towards their target. this keeps the overall progression speed the
                // same in dense areas.
                for (int i = 0; i < 3 && state.CurrentLocation != state.TargetLocation; i++)
                {
                    movementLog.Add(new()
                    {
                        PreviousLocation = bestPathToTargetLocation.Locations[i],
                        CurrentLocation = bestPathToTargetLocation.Locations[i + 1],
                    });
                    state = state with { CurrentLocation = movementLog[^1].CurrentLocation };
                    moved = true;
                }
            }

            if (!moved && state.StartledCounter == 0 && !state.CheckedLocations.Contains(state.CurrentLocation))
            {
                bool success = state.CurrentLocation.TryCheck(ref state);
                state = state with { LocationCheckAttemptsThisStep = state.LocationCheckAttemptsThisStep + 1 };
                if (!success)
                {
                    continue;
                }
            }

            if (bestTargetLocationReason == TargetLocationReason.Priority && state.CurrentLocation == state.TargetLocation)
            {
                // we've reached our next priority location. remove it from the queue.
                LocationDefinitionModel targetLocation = state.TargetLocation;
                state = state with { PriorityLocations = state.PriorityLocations.RemoveAll(l => l == targetLocation) };
            }

            // figure out if anything above changed our best target location. if not, then don't
            // update anything so that the Epoch will stay the same!
            bestTargetLocation = BestTargetLocation(state, out bestTargetLocationReason, out bestPathToTargetLocation);
            if (bestTargetLocation != state.TargetLocation)
            {
                state = state with { TargetLocation = bestTargetLocation };
            }
        }

        if (actionBalance > 0)
        {
            // it's possible to have a negative action counter due to a negative state.EnergyFactor,
            // and so we smear that move action across two rounds. but otherwise, this is very much
            // a "use it or lose it" system.
            actionBalance = 0;
        }

        if (state.LocationCheckAttemptsThisStep != 0)
        {
            state = state with { LocationCheckAttemptsThisStep = 0 };
        }

        if (state.ActionBalanceAfterPreviousStep != actionBalance)
        {
            state = state with { ActionBalanceAfterPreviousStep = actionBalance };
        }

        if (state.StartledCounter > 0)
        {
            state = state with { StartledCounter = state.StartledCounter - 1 };
            if (state.StartledCounter == 0)
            {
                LocationDefinitionModel bestTargetLocation = BestTargetLocation(state, out _, out _);
                if (bestTargetLocation != state.TargetLocation)
                {
                    state = state with { TargetLocation = bestTargetLocation };
                }
            }
        }

        if (movementLog.Count == 0)
        {
            if (state.PreviousStepMovementLog.Length > 1)
            {
                state = state with { PreviousStepMovementLog = [state.PreviousStepMovementLog[^1]] };
            }
        }
        else
        {
            state = state with { PreviousStepMovementLog = [.. movementLog] };
        }

        if (state.TargetLocationReason != bestTargetLocationReason)
        {
            state = state with { TargetLocationReason = bestTargetLocationReason };
        }

        return state;
    }

    private LocationDefinitionModel BestTargetLocation(GameState state, out TargetLocationReason reason, out ShortestPaths.Path bestPath)
    {
        if (state.StartledCounter > 0)
        {
            reason = TargetLocationReason.Startled;
            bestPath = state.CheckedLocations.ShortestPaths.GetPathOrNull(state.CurrentLocation, GameDefinitions.Instance.StartLocation)!.Value;
            return GameDefinitions.Instance.StartLocation;
        }

        foreach (LocationDefinitionModel priorityPriorityLocation in state.PriorityPriorityLocations)
        {
            if (state.ReceivedItems.ShortestPaths.GetPathOrNull(state.CurrentLocation, priorityPriorityLocation) is ShortestPaths.Path priorityPath)
            {
                reason = priorityPriorityLocation == GameDefinitions.Instance.GoalLocation
                    ? TargetLocationReason.GoMode
                    : TargetLocationReason.PriorityPriority;
                bestPath = state.CheckedLocations.ShortestPaths.GetPathOrNull(state.CurrentLocation, priorityPriorityLocation) ?? priorityPath;
                return
                    bestPath.Locations.FirstOrDefault(l => l.Region is LandmarkRegionDefinitionModel landmark && !state.CheckedLocations.Contains(landmark)) ??
                    bestPath.Locations[^1];
            }
        }

        foreach (LocationDefinitionModel priorityLocation in state.PriorityLocations)
        {
            if (state.ReceivedItems.ShortestPaths.GetPathOrNull(state.CurrentLocation, priorityLocation) is ShortestPaths.Path priorityPath)
            {
                reason = TargetLocationReason.Priority;
                bestPath = state.CheckedLocations.ShortestPaths.GetPathOrNull(state.CurrentLocation, priorityLocation) ?? priorityPath;
                return
                    bestPath.Locations.FirstOrDefault(l => l.Region is LandmarkRegionDefinitionModel landmark && !state.CheckedLocations.Contains(landmark)) ??
                    bestPath.Locations[^1];
            }
        }

        if (!state.CheckedLocations.Contains(state.CurrentLocation))
        {
            reason = TargetLocationReason.ClosestReachable;
            bestPath = state.ReceivedItems.ShortestPaths.GetPathOrNull(state.CurrentLocation, state.CurrentLocation)!.Value;
            return state.CurrentLocation;
        }

        PriorityQueue<LocationDefinitionModel, int> q = new();
        q.Enqueue(state.CurrentLocation, 0);
        HashSet<LocationDefinitionModel> settled = [];
        while (q.TryDequeue(out LocationDefinitionModel? nextLocation, out int distance))
        {
            if (!settled.Add(nextLocation))
            {
                continue;
            }

            if (!state.CheckedLocations.Contains(nextLocation))
            {
                reason = TargetLocationReason.ClosestReachable;
                bestPath = state.ReceivedItems.ShortestPaths.GetPathOrNull(state.CurrentLocation, nextLocation)!.Value;
                return nextLocation;
            }

            foreach (LocationDefinitionModel connected in GameDefinitions.Instance.ConnectedLocations[nextLocation])
            {
                if (settled.Contains(connected))
                {
                    // already fully handled this one.
                    continue;
                }

                if (connected.Region is LandmarkRegionDefinitionModel landmark && !landmark.Requirement.Satisfied(state.ReceivedItems))
                {
                    // can't actually reach it to check it.
                    continue;
                }

                q.Enqueue(connected, distance + 1);
            }
        }

        reason = TargetLocationReason.NowhereUsefulToMove;
        bestPath = state.ReceivedItems.ShortestPaths.GetPathOrNull(state.CurrentLocation, state.CurrentLocation)!.Value;
        return state.CurrentLocation;
    }
}
