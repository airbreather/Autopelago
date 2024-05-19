using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Autopelago;

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
            actionBalance = 0;
            state = state with { DistractionCounter = state.DistractionCounter - 1 };
        }

        while (actionBalance > 0 && !state.IsCompleted)
        {
            --actionBalance;

            LocationDefinitionModel bestTargetLocation = BestTargetLocation(state);
            if (state.TargetLocation != bestTargetLocation)
            {
                // changing your route takes an action
                state = state with { TargetLocation = bestTargetLocation };
                continue;
            }

            if (state.CurrentLocation != state.TargetLocation)
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

                state = state with { CurrentLocation = state.CurrentLocation.NextLocationTowards(state.TargetLocation, state) };

                // movement takes an action
                continue;
            }

            if (LocationIsChecked(state.CurrentLocation.Key))
            {
                // nowhere to move, nothing to do where we are.
                break;
            }

            bool success = state.CurrentLocation.TryCheck(ref state);
            state = state with { LocationCheckAttemptsThisStep = state.LocationCheckAttemptsThisStep + 1 };
            if (success)
            {
                MarkLocationChecked(state.CurrentLocation.Key);

                // pointing to the next target location on the current route does not take an action
                state = state with { TargetLocation = BestTargetLocation(state) };
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

        return state.Epoch == initialEpoch
            ? state
            : state with { TotalNontrivialStepCount = state.TotalNontrivialStepCount + 1 };
    }

    private bool LocationIsChecked(LocationKey key) => _checkedLocations[key.RegionKey][key.N];

    private void MarkLocationChecked(LocationKey key) => _checkedLocations[key.RegionKey][key.N] = true;

    private LocationDefinitionModel BestTargetLocation(GameState state)
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

        // TODO: this will still need a revamp, taking into account things like:
        // - requests / hints from other players
        LocationDefinitionModel? closestUncheckedLocation = state.CurrentLocation
            .EnumerateReachableLocationsByDistance(state)
            .FirstOrDefault(l => !LocationIsChecked(l.Location.Key))
            .Location;
        if (closestUncheckedLocation is null)
        {
            closestUncheckedLocation = state.CurrentLocation;
        }

        return closestUncheckedLocation;
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
