using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Autopelago;

public enum UncheckedLandmarkBehavior
{
    DoNotPassThrough,
    PassThroughIfRequirementsSatisfied,
    AlwaysPassThrough,
}

public sealed record GameState
{
    private GameState()
    {
    }

    public required ImmutableArray<LocationVector> PreviousStepMovementLog { get; init; }

    public required LocationDefinitionModel CurrentLocation { get; init; }

    public required LocationDefinitionModel TargetLocation { get; init; }

    public required ReceivedItems ReceivedItems { get; init; }

    public required CheckedLocations CheckedLocations { get; init; }

    public required ImmutableList<LocationDefinitionModel> PriorityPriorityLocations { get; init; }

    public required ImmutableList<LocationDefinitionModel> PriorityLocations { get; init; }

    public required int FoodFactor { get; init; }

    public required int LuckFactor { get; init; }

    public required int EnergyFactor { get; init; }

    public required int StyleFactor { get; init; }

    public required int DistractionCounter { get; init; }

    public required int StartledCounter { get; init; }

    public required bool HasConfidence { get; init; }

    public required int LocationCheckAttemptsThisStep { get; init; }

    public required int ActionBalanceAfterPreviousStep { get; init; }

    public required Prng.State PrngState { get; init; }

    public bool IsCompleted => CurrentLocation == GameDefinitions.Instance.GoalLocation;

    public int DiceModifier => (ReceivedItems.RatCount / 3) - (LocationCheckAttemptsThisStep * 5);

    public static GameState Start(Random? random = null)
    {
        return Start(Prng.State.Start(random));
    }

    public static GameState Start(ulong seed)
    {
        return Start(Prng.State.Start(seed));
    }

    public static GameState Start(Prng.State prngState)
    {
        return new()
        {
            PreviousStepMovementLog = [],
            CurrentLocation = GameDefinitions.Instance.StartLocation,
            TargetLocation = GameDefinitions.Instance.StartLocation,
            ReceivedItems = new() { InReceivedOrder = [] },
            CheckedLocations = new() { InCheckedOrder = [] },
            PriorityPriorityLocations = [],
            PriorityLocations = [],
            FoodFactor = 0,
            LuckFactor = 0,
            EnergyFactor = 0,
            StyleFactor = 0,
            DistractionCounter = 0,
            StartledCounter = 0,
            HasConfidence = false,
            LocationCheckAttemptsThisStep = 0,
            ActionBalanceAfterPreviousStep = 0,
            PrngState = prngState,
        };
    }

    public static int NextD20(ref GameState state)
    {
        Prng.State s = state.PrngState;
        int result = Prng.NextD20(ref s);
        state = state with { PrngState = s };
        return result;
    }

    public void VisitLocationsByDistanceFromCurrentLocation(LocationVisitor visitor, UncheckedLandmarkBehavior uncheckedLandmarkBehavior)
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
        FrozenSet<LocationDefinitionModel> checkedLocations = CheckedLocations.AsFrozenSet;
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
                    alreadyChecked: checkedLocations.Contains(curr)))
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

    public bool Equals(GameState? other)
    {
        return
            other is not null &&
            PrngState == other.PrngState &&
            PreviousStepMovementLog.SequenceEqual(other.PreviousStepMovementLog) &&
            CurrentLocation == other.CurrentLocation &&
            TargetLocation == other.TargetLocation &&
            LocationCheckAttemptsThisStep == other.LocationCheckAttemptsThisStep &&
            ActionBalanceAfterPreviousStep == other.ActionBalanceAfterPreviousStep &&
            FoodFactor == other.FoodFactor &&
            LuckFactor == other.LuckFactor &&
            EnergyFactor == other.EnergyFactor &&
            StyleFactor == other.StyleFactor &&
            DistractionCounter == other.DistractionCounter &&
            StartledCounter == other.StartledCounter &&
            HasConfidence == other.HasConfidence &&
            ReceivedItems == other.ReceivedItems &&
            CheckedLocations.InCheckedOrder.SequenceEqual(other.CheckedLocations.InCheckedOrder) &&
            PriorityPriorityLocations.SequenceEqual(other.PriorityPriorityLocations) &&
            PriorityLocations.SequenceEqual(other.PriorityLocations);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            HashCode.Combine(
                PrngState,
                PreviousStepMovementLog.Length,
                CurrentLocation,
                TargetLocation,
                LocationCheckAttemptsThisStep,
                ActionBalanceAfterPreviousStep),
            HashCode.Combine(
                FoodFactor,
                LuckFactor,
                EnergyFactor,
                StyleFactor,
                DistractionCounter,
                StartledCounter),
            HashCode.Combine(
                HasConfidence,
                ReceivedItems,
                CheckedLocations.Count,
                PriorityPriorityLocations.Count,
                PriorityLocations.Count)
        );
    }
}
