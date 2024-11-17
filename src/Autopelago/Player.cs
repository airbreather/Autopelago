using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Autopelago;

public enum BestTargetLocationReason
{
    ClosestReachable,
    Priority,
    Startled,
    GoMode,
}

public sealed class Player
{
    private static readonly ImmutableArray<ImmutableArray<LandmarkRegionDefinitionModel>> s_allGoModePaths = ComputeAllGoModePaths();

    private readonly FrozenDictionary<string, BitArray> _checkedLocations = GameDefinitions.Instance.AllRegions.ToFrozenDictionary(kvp => kvp.Key, kvp => new BitArray(kvp.Value.Locations.Length));

    private readonly Dictionary<ItemDefinitionModel, int> _receivedItemsMap = [];

    public GameState Advance(GameState state)
    {
        ulong initialEpoch = state.Epoch;

        if (state.IsCompleted)
        {
            return state;
        }

        foreach (BitArray isChecked in _checkedLocations.Values)
        {
            isChecked.SetAll(false);
        }

        foreach (LocationDefinitionModel checkedLocation in state.CheckedLocations)
        {
            MarkLocationChecked(checkedLocation.Key);
        }

        _receivedItemsMap.Clear();
        foreach (ItemDefinitionModel receivedItem in state.ReceivedItems)
        {
            ++CollectionsMarshal.GetValueRefOrAddDefault(_receivedItemsMap, receivedItem, out _);
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

        LocationDefinitionModel previousLocation = state.PreviousLocation;
        while (actionBalance > 0 && !state.IsCompleted)
        {
            --actionBalance;

            LocationDefinitionModel bestTargetLocation = BestTargetLocation(state, out BestTargetLocationReason bestTargetLocationReason);
            if (state.TargetLocation != bestTargetLocation)
            {
                // changing your route takes an action...
                state = state with { TargetLocation = bestTargetLocation };

                // ...unless you're startled, in which case it was instinct.
                if (bestTargetLocationReason != BestTargetLocationReason.Startled)
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
                    previousLocation = state.CurrentLocation;
                    state = state with { CurrentLocation = state.CurrentLocation.NextLocationTowards(state.TargetLocation, state) };
                    moved = true;
                    bestTargetLocation = BestTargetLocation(state, out bestTargetLocationReason);
                    if (state.TargetLocation != bestTargetLocation)
                    {
                        state = state with { TargetLocation = bestTargetLocation };
                    }
                }
            }

            if (!moved && state.StartledCounter == 0 && !LocationIsChecked(state.CurrentLocation.Key))
            {
                bool success = state.CurrentLocation.TryCheck(ref state);
                state = state with { LocationCheckAttemptsThisStep = state.LocationCheckAttemptsThisStep + 1 };
                if (!success)
                {
                    continue;
                }

                MarkLocationChecked(state.CurrentLocation.Key);
            }

            if (bestTargetLocationReason == BestTargetLocationReason.Priority && state.CurrentLocation == state.TargetLocation)
            {
                // we've reached our next priority location. remove it from the queue.
                state = state with { PriorityLocations = state.PriorityLocations.RemoveAll(l => l.Location == state.TargetLocation) };
            }

            // figure out if anything above changed our best target location. if not, then don't
            // update anything so that the Epoch will stay the same!
            bestTargetLocation = BestTargetLocation(state, out bestTargetLocationReason);
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
                LocationDefinitionModel bestTargetLocation = BestTargetLocation(state, out _);
                if (bestTargetLocation != state.TargetLocation)
                {
                    state = state with { TargetLocation = bestTargetLocation };
                }
            }
        }

        if (state.PreviousLocation != previousLocation)
        {
            state = state with { PreviousLocation = previousLocation };
        }

        return state.Epoch == initialEpoch
            ? state
            : state with { TotalNontrivialStepCount = state.TotalNontrivialStepCount + 1 };
    }

    public LocationDefinitionModel? NextGoModeLocation(GameState state)
    {
        List<(LocationDefinitionModel Location, int Depth)> goModeTargets = [];
        HashSet<string> satisfiedLandmarks = [];
        HashSet<string> unsatisfiedLandmarks = [];
        foreach (ImmutableArray<LandmarkRegionDefinitionModel> goModePath in s_allGoModePaths)
        {
            ImmutableList<ItemDefinitionModel> receivedItems = state.ReceivedItems;
            foreach (LandmarkRegionDefinitionModel region in goModePath)
            {
                if (unsatisfiedLandmarks.Contains(region.Key))
                {
                    goto nextGoModePath;
                }

                if (satisfiedLandmarks.Contains(region.Key))
                {
                    if (region.Locations[0].RewardIsFixed)
                    {
                        receivedItems = receivedItems.Add(region.Locations[0].UnrandomizedItem!);
                    }

                    continue;
                }

                if (!(region.Requirement.Satisfied(state with { ReceivedItems = receivedItems })))
                {
                    unsatisfiedLandmarks.Add(region.Key);
                    goto nextGoModePath;
                }

                satisfiedLandmarks.Add(region.Key);
                if (region.Locations[0].RewardIsFixed)
                {
                    receivedItems = receivedItems.Add(region.Locations[0].UnrandomizedItem!);
                }
            }

            for (int i = 0; i < goModePath.Length; i++)
            {
                LocationDefinitionModel goModeTarget = goModePath[i].Locations[0];
                if (!LocationIsChecked(goModeTarget.Key))
                {
                    goModeTargets.Add((goModeTarget, i));
                    goto nextGoModePath;
                }
            }

            return GameDefinitions.Instance.GoalLocation;
        nextGoModePath:;
        }

        if (goModeTargets.Count > 0)
        {
            return goModeTargets.MaxBy(tgt => tgt.Depth).Location;
        }

        return null;
    }

    private bool LocationIsChecked(LocationKey key) => _checkedLocations[key.RegionKey][key.N];

    private void MarkLocationChecked(LocationKey key) => _checkedLocations[key.RegionKey][key.N] = true;

    public LocationDefinitionModel BestTargetLocation(GameState state, out BestTargetLocationReason reason)
    {
        if (state.StartledCounter > 0)
        {
            reason = BestTargetLocationReason.Startled;
            return state.CurrentLocation.NextLocationTowards(GameDefinitions.Instance.StartLocation, state);
        }

        if (NextGoModeLocation(state) is { } nextGoModeLocation)
        {
            reason = BestTargetLocationReason.GoMode;
            return nextGoModeLocation;
        }

        if (BestPriorityLocation(state) is { } bestPriorityLocation)
        {
            reason = BestTargetLocationReason.Priority;
            return bestPriorityLocation;
        }

        LocationDefinitionModel? closestUncheckedLocation = state.CurrentLocation
            .EnumerateReachableLocationsByDistance(state)
            .FirstOrDefault(l => !LocationIsChecked(l.Location.Key))
            .Location;
        if (closestUncheckedLocation is null)
        {
            closestUncheckedLocation = state.CurrentLocation;
        }

        reason = BestTargetLocationReason.ClosestReachable;
        return closestUncheckedLocation;
    }

    private LocationDefinitionModel? BestPriorityLocation(GameState state)
    {
        if (state.PriorityLocations.IsEmpty)
        {
            return null;
        }

        foreach (PriorityLocationModel priorityLocation in state.PriorityLocations)
        {
            foreach ((LocationDefinitionModel reachableLocation, ImmutableList<LocationDefinitionModel> path) in state.CurrentLocation.EnumerateReachableLocationsByDistance(state))
            {
                if (reachableLocation.Key != priorityLocation.Location.Key)
                {
                    continue;
                }

                // #45: the current priority location may be "reachable" in some sense, but the path
                // to it may include one or more clearABLE landmarks that haven't been clearED yet.
                foreach (LocationDefinitionModel nextLocation in path.Prepend(state.CurrentLocation))
                {
                    if (nextLocation.Region is LandmarkRegionDefinitionModel && !LocationIsChecked(nextLocation.Key))
                    {
                        return nextLocation;
                    }
                }

                // if we've made it here, then the whole path is open. you're all clear, kid, now
                // let's check this thing and go home!
                return reachableLocation;
            }
        }

        return null;
    }

    private static ImmutableArray<ImmutableArray<LandmarkRegionDefinitionModel>> ComputeAllGoModePaths()
    {
        Queue<(RegionDefinitionModel Region, ImmutableList<LandmarkRegionDefinitionModel> Landmarks)> regionsQueue = new();
        regionsQueue.Enqueue((GameDefinitions.Instance.StartRegion, []));
        List<ImmutableArray<LandmarkRegionDefinitionModel>> paths = [];
        while (regionsQueue.TryDequeue(out (RegionDefinitionModel Region, ImmutableList<LandmarkRegionDefinitionModel> Landmarks) next))
        {
            (RegionDefinitionModel nextRegion, ImmutableList<LandmarkRegionDefinitionModel> incomingLandmarks) = next;
            if (nextRegion == GameDefinitions.Instance.GoalRegion)
            {
                paths.Add([.. incomingLandmarks]);
                continue;
            }

            if (nextRegion is LandmarkRegionDefinitionModel landmark)
            {
                incomingLandmarks = incomingLandmarks.Add(landmark);
            }

            foreach (RegionExitDefinitionModel exit in nextRegion.Exits)
            {
                regionsQueue.Enqueue((exit.Region, incomingLandmarks));
            }
        }

        return [.. paths];
    }
}
