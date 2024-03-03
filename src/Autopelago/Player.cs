using System.Runtime.InteropServices;

namespace Autopelago;

public sealed class Player
{
    private readonly HashSet<LocationDefinitionModel> _checkedLocations = [];

    private readonly Dictionary<ItemDefinitionModel, int> _receivedItemsMap = [];

    public Game.State Advance(Game.State state)
    {
        _checkedLocations.Clear();
        _checkedLocations.UnionWith(state.CheckedLocations);

        _receivedItemsMap.Clear();
        foreach (ItemDefinitionModel receivedItem in state.ReceivedItems)
        {
            ++CollectionsMarshal.GetValueRefOrAddDefault(_receivedItemsMap, receivedItem, out _);
        }

        for (int actionsRemainingThisStep = 3; actionsRemainingThisStep > 0; --actionsRemainingThisStep)
        {
            LocationDefinitionModel bestTargetLocation = BestTargetLocation(state);
            if (state.TargetLocation != bestTargetLocation)
            {
                // changing your route takes an action
                state = state with { TargetLocation = bestTargetLocation };
                continue;
            }

            if (state.CurrentLocation != state.TargetLocation)
            {
                state = state with { CurrentLocation = state.CurrentLocation.NextLocationTowards(state.TargetLocation) };

                // movement takes an action
                continue;
            }

            if (_checkedLocations.Contains(state.CurrentLocation))
            {
                // nowhere to move, nothing to do where we are.
                break;
            }

            bool success = state.CurrentLocation.TryCheck(ref state);
            state = state with { LocationCheckAttemptsThisStep = state.LocationCheckAttemptsThisStep + 1 };
            if (success)
            {
                _checkedLocations.Add(state.CurrentLocation);

                // pointing to the next target location on the current route does not take an action
                state = state with { TargetLocation = BestTargetLocation(state) };
            }
        }

        if (state.LocationCheckAttemptsThisStep > 0)
        {
            state = state with { LocationCheckAttemptsThisStep = 0 };
        }

        return state;
    }

    private LocationDefinitionModel BestTargetLocation(Game.State state)
    {
        // TODO: *much* of this will need a revamp, taking into account things like:
        // - we want to be "stupid" by default until the player collects a "make us smarter" rat.
        // - "go mode"
        // - requests / hints from other players
        LocationDefinitionModel result = state.TargetLocation;
        int bestDistanceSoFar = state.CurrentLocation.DistanceTo(result);
        if (state.CurrentLocation == result)
        {
            if (_checkedLocations.Contains(result))
            {
                bestDistanceSoFar = int.MaxValue;
            }
            else
            {
                // TODO: this seems *somewhat* durable, but we will stil need to account for "go mode"
                // once that concept comes back in this.
                return result;
            }
        }

        HashSet<RegionDefinitionModel> enqueued = [];
        Queue<RegionDefinitionModel> regionsToCheck = new();
        Enqueue(GameDefinitions.Instance.Regions.StartRegion);
        while (regionsToCheck.TryDequeue(out RegionDefinitionModel? region))
        {
            foreach (LocationDefinitionModel candidate in region.Locations)
            {
                if (!_checkedLocations.Contains(candidate) && candidate.Requirement.StaticSatisfied(state))
                {
                    int distance = state.CurrentLocation.DistanceTo(candidate);
                    if (distance < bestDistanceSoFar)
                    {
                        result = candidate;
                        bestDistanceSoFar = distance;
                    }
                }
            }

            foreach (RegionExitDefinitionModel exit in region.Exits)
            {
                if (exit.Requirement.StaticSatisfied(state))
                {
                    Enqueue(exit.Region);
                }
            }
        }

        return result;

        void Enqueue(RegionDefinitionModel region)
        {
            if (enqueued.Add(region))
            {
                regionsToCheck.Enqueue(region);
            }
        }
    }
}
