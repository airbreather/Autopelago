using System.Collections;
using System.Runtime.InteropServices;

public sealed class Player
{
    private readonly Dictionary<RegionDefinitionModel, BitArray> _checkedLocations;

    private readonly Dictionary<ItemDefinitionModel, int> _receivedItemsMap = [];

    public Player()
    {
        _checkedLocations = GameDefinitions.Instance.Regions.AllRegions.Values.ToDictionary(r => r, r => new BitArray(r.Locations.Length));
    }

    public Game.State Advance(Game.State state)
    {
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
                state = state with { Epoch = state.Epoch + 1, TargetLocation = bestTargetLocation };
                continue;
            }

            if (state.CurrentLocation != state.TargetLocation)
            {
                state = state with { Epoch = state.Epoch + 1, CurrentLocation = GameDefinitions.Instance.FloydWarshall.GetPath(state.CurrentLocation, state.TargetLocation)[1] };

                // movement takes an action
                continue;
            }

            if (state.CurrentLocation.Requirement.DynamicSatisfied(ref state))
            {
                state = state with { Epoch = state.Epoch + 1, CheckedLocations = state.CheckedLocations.Add(state.CurrentLocation) };
                _checkedLocations[state.CurrentLocation.Region][state.CurrentLocation.Key.N] = true;
            }
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
        if (state.CurrentLocation == result && !_checkedLocations[result.Region][result.Key.N])
        {
            // TODO: this seems *somewhat* durable, but we will stil need to account for "go mode"
            // once that concept comes back in this.
            return result;
        }

        HashSet<RegionDefinitionModel> enqueued = [];
        Queue<RegionDefinitionModel> regionsToCheck = new();
        Enqueue(GameDefinitions.Instance.Regions.StartRegion);
        int bestDistanceSoFar = GameDefinitions.Instance.FloydWarshall.GetDistance(state.CurrentLocation, result);
        while (regionsToCheck.TryDequeue(out RegionDefinitionModel? region))
        {
            BitArray checkedInThisRegion = _checkedLocations[region];
            if (!checkedInThisRegion.HasAllSet())
            {
                foreach (LocationDefinitionModel candidate in region.Locations)
                {
                    if (!checkedInThisRegion[candidate.Key.N] && candidate.Requirement.StaticSatisfied(state))
                    {
                        int distance = GameDefinitions.Instance.FloydWarshall.GetDistance(state.CurrentLocation, candidate);
                        if (distance < bestDistanceSoFar)
                        {
                            result = candidate;
                            bestDistanceSoFar = distance;
                        }
                    }
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
