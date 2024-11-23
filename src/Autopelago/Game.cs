using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;

namespace Autopelago;

public enum UncheckedLandmarkBehavior
{
    DoNotPassThrough,
    PassThroughIfRequirementsSatisfied,
    AlwaysPassThrough,
}

public enum TargetLocationReason
{
    GameNotStarted,
    NowhereUsefulToMove,
    ClosestReachable,
    Priority,
    PriorityPriority,
    GoMode,
    Startled,
}

public sealed record LocationVector
{
    public required LocationDefinitionModel PreviousLocation { get; init; }

    public required LocationDefinitionModel CurrentLocation { get; init; }
}

public sealed class Game
{
    private ShortestPaths.Path _pathToTargetLocation = ShortestPaths.Path.Only(GameDefinitions.Instance.StartLocation);

    private int _locationCheckAttemptsThisStep;

    private int _actionBalanceAfterPreviousStep;

    public Game(Prng.State prngState)
        : this(prngState, GameDefinitions.Instance.LocationsByName.Values.ToFrozenDictionary(l => l, l => l.UnrandomizedItem?.ArchipelagoFlags ?? ArchipelagoItemFlags.None))
    {
    }

    public Game(Prng.State prngState, FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags> spoilerData)
    {
        PrngState = prngState;
        SpoilerData = spoilerData;
    }

    public FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags> SpoilerData { get; private set; }

    public ImmutableArray<LocationVector> PreviousStepMovementLog { get; private set; } = [];

    public LocationDefinitionModel CurrentLocation { get; private set; } = GameDefinitions.Instance.StartLocation;

    public LocationDefinitionModel TargetLocation { get; private set; } = GameDefinitions.Instance.StartLocation;

    public TargetLocationReason TargetLocationReason { get; private set; } = TargetLocationReason.GameNotStarted;

    public ReceivedItems ReceivedItems { get; private set; } = new() { InReceivedOrder = [] };

    public CheckedLocations CheckedLocations { get; private set; } = new() { InCheckedOrder = [] };

    public ImmutableList<LocationDefinitionModel> PriorityPriorityLocations { get; private set; } = [GameDefinitions.Instance.GoalLocation];

    public ImmutableList<LocationDefinitionModel> PriorityLocations { get; private set; } = [];

    public int FoodFactor { get; private set; }

    public int LuckFactor { get; private set; }

    public int EnergyFactor { get; private set; }

    public int StyleFactor { get; private set; }

    public int DistractionCounter { get; private set; }

    public int StartledCounter { get; private set; }

    public bool HasConfidence { get; private set; }

    public Prng.State PrngState { get; private set; }

    public bool IsCompleted => CurrentLocation == GameDefinitions.Instance.GoalLocation;

    private int DiceModifier => (ReceivedItems.RatCount / 3) - (_locationCheckAttemptsThisStep * 5);

    public void ArbitrarilyModifyState<T>(Expression<Func<Game, T>> prop, T value)
    {
        ((PropertyInfo)(((MemberExpression)prop.Body).Member)).SetValue(this, value);
    }

    public bool AddPriorityLocation(LocationDefinitionModel toPrioritize)
    {
        if (PriorityLocations.Contains(toPrioritize))
        {
            return false;
        }

        PriorityLocations = PriorityLocations.Add(toPrioritize);
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
        PriorityLocations = PriorityLocations.RemoveAt(index);
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
            if (uncheckedLocationsToIgnore.Contains(curr) || curr.RewardIsFixed || SpoilerData[curr] != flags)
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
                        VisitLocationsByDistanceFromCurrentLocation(smartVisitor, UncheckedLandmarkBehavior.PassThroughIfRequirementsSatisfied);
                        break;

                    case "conspiratorial":
                        VisitLocationsByDistanceFromCurrentLocation(conspiratorialVisitor, UncheckedLandmarkBehavior.PassThroughIfRequirementsSatisfied);
                        break;

                    case "confident":
                        addConfidence = true;
                        break;
                }
            }

            // subtract first
            if (subtractConfidence)
            {
                HasConfidence = false;
            }

            if (addConfidence)
            {
                HasConfidence = true;
            }
        }

        ReceivedItems = new() { InReceivedOrder = ReceivedItems.InReceivedOrder.AddRange(newItems) };
        FoodFactor += (foodMod * 5);
        EnergyFactor += (energyFactorMod * 5);
        LuckFactor += luckFactorMod;
        StyleFactor += (stylishMod * 2);
        DistractionCounter += distractedMod;
        StartledCounter += startledMod;
        PriorityPriorityLocations = PriorityPriorityLocations.AddRange(priorityPriorityLocations);
    }

    public void Advance()
    {
        if (IsCompleted)
        {
            return;
        }

        int actionBalance = 3 + _actionBalanceAfterPreviousStep;
        switch (FoodFactor)
        {
            case < 0:
                --actionBalance;
                FoodFactor += 1;
                break;

            case > 0:
                ++actionBalance;
                FoodFactor -= 1;
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

            DistractionCounter -= 1;
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
                        EnergyFactor += 1;
                        break;

                    case > 0:
                        ++actionBalance;
                        EnergyFactor -= 1;
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
                        PreviousLocation = _pathToTargetLocation.Locations[i],
                        CurrentLocation = _pathToTargetLocation.Locations[i + 1],
                    });
                    CurrentLocation = movementLog[^1].CurrentLocation;
                    moved = true;
                }
            }

            if (!moved && StartledCounter == 0 && !CheckedLocations.Contains(CurrentLocation))
            {
                bool success = TryCheck(CurrentLocation);
                _locationCheckAttemptsThisStep += 1;
                if (!success)
                {
                    continue;
                }
            }

            if (CurrentLocation == TargetLocation)
            {
                LocationDefinitionModel targetLocation = TargetLocation;
                switch (TargetLocationReason)
                {
                    case TargetLocationReason.Priority:
                        PriorityLocations = PriorityLocations.RemoveAll(l => l == targetLocation);
                        break;

                    case TargetLocationReason.PriorityPriority:
                        PriorityPriorityLocations = PriorityPriorityLocations.RemoveAll(l => l == targetLocation);
                        break;
                }
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
            StartledCounter -= 1;
            UpdateTargetLocation();
        }

        if (movementLog.Count == 0 && PreviousStepMovementLog.Length > 1)
        {
            PreviousStepMovementLog = [PreviousStepMovementLog[^1]];
        }
        else
        {
            PreviousStepMovementLog = [.. movementLog];
        }

        _actionBalanceAfterPreviousStep = actionBalance;
        _locationCheckAttemptsThisStep = 0;
    }

    private bool UpdateTargetLocation()
    {
        LocationDefinitionModel prevTargetLocation = TargetLocation;
        TargetLocation = BestTargetLocation(out TargetLocationReason bestTargetLocationReason, out ShortestPaths.Path bestPathToTargetLocation);
        TargetLocationReason = bestTargetLocationReason;
        _pathToTargetLocation = bestPathToTargetLocation;
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
        switch (LuckFactor)
        {
            case < 0:
                extraDiceModifier -= 5;
                LuckFactor += 1;
                break;

            case > 0:
                LuckFactor -= 1;
                goto success;
        }

        if (StyleFactor > 0)
        {
            extraDiceModifier += 5;
            StyleFactor -= 1;
        }

        if (NextD20() + DiceModifier + extraDiceModifier < location.AbilityCheckDC)
        {
            return false;
        }

        success:
        CheckedLocations = new() { InCheckedOrder = CheckedLocations.InCheckedOrder.Add(location) };
        return true;
    }

    private void VisitLocationsByDistanceFromCurrentLocation(LocationVisitor visitor, UncheckedLandmarkBehavior uncheckedLandmarkBehavior)
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
                    alreadyChecked: CheckedLocations.Contains(curr)))
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

    private int NextD20()
    {
        Prng.State s = PrngState;
        int result = Prng.NextD20(ref s);
        PrngState = s;
        return result;
    }
}
