using System.Collections;
using System.Collections.Frozen;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Autopelago;

public sealed class Player
{
    private readonly FrozenDictionary<string, BitArray> _checkedLocations = GameDefinitions.Instance.Regions.AllRegions.ToFrozenDictionary(kvp => kvp.Key, kvp => new BitArray(kvp.Value.Locations.Length));

    private readonly Dictionary<ItemDefinitionModel, int> _receivedItemsMap = [];

    public Game.State Advance(Game.State state)
    {
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

        if (state.LocationCheckAttemptsThisStep > 0)
        {
            state = state with { LocationCheckAttemptsThisStep = 0 };
        }

        return state;
    }

    private static ulong StepsToBK(Game.State state)
    {
        Player player = new();
        ulong orig = state.Epoch;
        while (true)
        {
            ulong prev = state.Epoch;
            state = player.Advance(state);
            if (state.Epoch == prev)
            {
                return prev - orig;
            }
        }
    }

    private bool LocationIsChecked(LocationKey key) => _checkedLocations[key.RegionKey][key.N];

    private void MarkLocationChecked(LocationKey key) => _checkedLocations[key.RegionKey][key.N] = true;

    private LocationDefinitionModel BestTargetLocation(Game.State state)
    {
        // TODO: *much* of this will need a revamp, taking into account things like:
        // - we want to be "stupid" by default until the player collects a "make us smarter" rat.
        // - "go mode"
        // - requests / hints from other players
        int bestDistanceSoFar = state.CurrentLocation.DistanceTo(state.TargetLocation);
        if (state.CurrentLocation == state.TargetLocation)
        {
            if (LocationIsChecked(state.TargetLocation.Key))
            {
                bestDistanceSoFar = int.MaxValue;
            }
            else
            {
                // TODO: this seems *somewhat* durable, but we will stil need to account for "go mode"
                // once that concept comes back in this.
                return state.TargetLocation;
            }
        }

        HashSet<LocationDefinitionModel> candidates = [state.TargetLocation];
        HashSet<RegionDefinitionModel> enqueued = [];
        Queue<RegionDefinitionModel> regionsToCheck = new();
        Enqueue(GameDefinitions.Instance.StartRegion);
        while (regionsToCheck.TryDequeue(out RegionDefinitionModel? region))
        {
            BitArray regionCheckedLocations = _checkedLocations[region.Key];
            if (!regionCheckedLocations.HasAllSet())
            {
                foreach (LocationDefinitionModel candidate in region.Locations)
                {
                    if (!regionCheckedLocations[candidate.Key.N] && candidate.Requirement.StaticSatisfied(state))
                    {
                        int distance = state.CurrentLocation.DistanceTo(candidate);
                        if (distance <= bestDistanceSoFar)
                        {
                            if (distance < bestDistanceSoFar)
                            {
                                candidates.Clear();
                                bestDistanceSoFar = distance;
                            }

                            candidates.Add(candidate);
                        }
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

        if (candidates.Count == 1)
        {
            return candidates.First();
        }

        // distinguish by whichever leads to the end of everything that we can do most efficiently.
        return (
            from candidate in candidates.AsParallel()
            let candidateState = state with { CurrentLocation = state.CurrentLocation.NextLocationTowards(candidate), TargetLocation = candidate }
            from i in Enumerable.Range(0, Environment.ProcessorCount)
            let iterState = candidateState with { PrngState = Prng.ShortJumped(candidateState.PrngState) }
            group StepsToBK(iterState) by candidate into grp
            let stepCount = grp.Aggregate((x, y) => x + y)
            select new
            {
                TargetLocation = grp.Key,
                StepCount = stepCount,
            }
        ).MinBy(x => x.StepCount)!.TargetLocation;

        void Enqueue(RegionDefinitionModel region)
        {
            if (enqueued.Add(region))
            {
                regionsToCheck.Enqueue(region);
            }
        }
    }
}
