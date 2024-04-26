using System.Collections;
using System.Collections.Frozen;
using System.Runtime.InteropServices;

namespace Autopelago;

public sealed class Player
{
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
                    if (!regionCheckedLocations[candidate.Key.N] && candidate.Requirement.Satisfied(state))
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
                if (exit.Requirement.Satisfied(state))
                {
                    Enqueue(exit.Region);
                }
            }
        }

        return candidates.First();

        void Enqueue(RegionDefinitionModel region)
        {
            if (enqueued.Add(region))
            {
                regionsToCheck.Enqueue(region);
            }
        }
    }
}
