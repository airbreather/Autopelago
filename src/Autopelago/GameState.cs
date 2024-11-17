using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Autopelago;

public sealed record PriorityLocationModel
{
    public required LocationDefinitionModel Location { get; init; }

    public required SourceKind Source { get; init; }

    public PriorityLocationModelProxy ToProxy()
    {
        return new()
        {
            Location = Location.Name,
            Source = Source,
        };
    }

    public enum SourceKind
    {
        Player,
        Smart,
        Conspiratorial,
    }

    public sealed record PriorityLocationModelProxy
    {
        public required string Location { get; init; }

        public required SourceKind Source { get; init; }

        public PriorityLocationModel ToPriorityLocation()
        {
            return new()
            {
                Location = GameDefinitions.Instance.LocationsByName[Location],
                Source = Source,
            };
        }
    }
}

public sealed record GameState
{
    private GameState()
    {
    }

    private GameState(GameState copyFrom)
    {
        Epoch = copyFrom.Epoch + 1;
        TotalNontrivialStepCount = copyFrom.TotalNontrivialStepCount;
        PreviousLocation = copyFrom.PreviousLocation;
        CurrentLocation = copyFrom.CurrentLocation;
        TargetLocation = copyFrom.TargetLocation;
        ReceivedItems = copyFrom.ReceivedItems;
        CheckedLocations = copyFrom.CheckedLocations;
        PriorityLocations = copyFrom.PriorityLocations;
        FoodFactor = copyFrom.FoodFactor;
        LuckFactor = copyFrom.LuckFactor;
        EnergyFactor = copyFrom.EnergyFactor;
        StyleFactor = copyFrom.StyleFactor;
        DistractionCounter = copyFrom.DistractionCounter;
        StartledCounter = copyFrom.StartledCounter;
        HasConfidence = copyFrom.HasConfidence;
        LocationCheckAttemptsThisStep = copyFrom.LocationCheckAttemptsThisStep;
        ActionBalanceAfterPreviousStep = copyFrom.ActionBalanceAfterPreviousStep;
        PrngState = copyFrom.PrngState;
    }

    public ulong Epoch { get; private init; }

    public required ulong TotalNontrivialStepCount { get; init; }

    public required LocationDefinitionModel PreviousLocation { get; init; }

    public required LocationDefinitionModel CurrentLocation { get; init; }

    public required LocationDefinitionModel TargetLocation { get; init; }

    public required ImmutableList<ItemDefinitionModel> ReceivedItems { get; init; }

    public required ImmutableList<LocationDefinitionModel> CheckedLocations { get; init; }

    public required ImmutableList<PriorityLocationModel> PriorityLocations { get; init; }

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

    public double IntervalDurationMultiplier => 1;

    public bool IsCompleted => CurrentLocation == GameDefinitions.Instance.GoalLocation;

    public int DiceModifier => (RatCount / 3) - (LocationCheckAttemptsThisStep * 5);

    public int RatCount => ReceivedItems.Sum(i => i.RatCount).GetValueOrDefault();

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
            TotalNontrivialStepCount = 0,
            PreviousLocation = GameDefinitions.Instance.StartLocation,
            CurrentLocation = GameDefinitions.Instance.StartLocation,
            TargetLocation = GameDefinitions.Instance.StartLocation,
            ReceivedItems = [],
            CheckedLocations = [],
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

    public IEnumerable<RegionDefinitionModel> EnumerateOpenRegions()
    {
        Queue<RegionDefinitionModel> regionsQueue = new();
        regionsQueue.Enqueue(GameDefinitions.Instance.StartRegion);
        HashSet<RegionDefinitionModel> seenRegions = [];
        while (regionsQueue.TryDequeue(out RegionDefinitionModel? nextRegion))
        {
            yield return nextRegion;

            foreach (RegionExitDefinitionModel exit in nextRegion.Exits)
            {
                RegionDefinitionModel exitRegion = exit.Region;
                if (!seenRegions.Add(exit.Region))
                {
                    continue;
                }

                if (exitRegion is LandmarkRegionDefinitionModel { Requirement: { } req } && !req.Satisfied(this))
                {
                    continue;
                }

                regionsQueue.Enqueue(exit.Region);
            }
        }
    }

    public GameState ResolveSmartAndConspiratorialAuras(ReadOnlySpan<PriorityLocationModel.SourceKind> receivedAuras, FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags> spoilerData, out ImmutableArray<PriorityLocationModel> resolvedLocations)
    {
        if (receivedAuras.IsEmpty)
        {
            resolvedLocations = [];
            return this;
        }

        HashSet<LocationDefinitionModel> locationsToIgnore =
        [
            .. CheckedLocations,
            .. PriorityLocations.Select(p => p.Location),
        ];
        List<PriorityLocationModel> locationsToAppend = [];
        using IEnumerator<LocationDefinitionModel> smartTargets = Targets(ArchipelagoItemFlags.LogicalAdvancement).GetEnumerator();
        using IEnumerator<LocationDefinitionModel> conspiratorialTargets = Targets(ArchipelagoItemFlags.Trap).GetEnumerator();
        foreach (PriorityLocationModel.SourceKind receivedAura in receivedAuras)
        {
            IEnumerator<LocationDefinitionModel> appendFrom = receivedAura switch
            {
                PriorityLocationModel.SourceKind.Smart => smartTargets,
                PriorityLocationModel.SourceKind.Conspiratorial => conspiratorialTargets,
                _ => throw new ArgumentException("inputs need to be smart or conspiratorial", nameof(receivedAuras)),
            };

            if (!appendFrom.MoveNext())
            {
                // we've reached the end of everything that this aura can do for us. it fizzles.
                continue;
            }

            locationsToAppend.Add(new()
            {
                Location = appendFrom.Current,
                Source = receivedAura,
            });
        }

        resolvedLocations = [.. locationsToAppend];
        return this with { PriorityLocations = PriorityLocations.AddRange(locationsToAppend) };
        IEnumerable<LocationDefinitionModel> Targets(ArchipelagoItemFlags flags)
        {
            // go through all the reachable ones first
            foreach ((LocationDefinitionModel nxt, _) in CurrentLocation.EnumerateReachableLocationsByDistance(this))
            {
                if (!nxt.RewardIsFixed && (spoilerData[nxt] & flags) != ArchipelagoItemFlags.None && locationsToIgnore.Add(nxt))
                {
                    yield return nxt;
                }
            }

            // don't let the aura fizzle just because nothing's immediately reachable, though.
            Queue<LocationDefinitionModel> q = new([CurrentLocation]);
            HashSet<LocationDefinitionModel> queued = [];
            while (q.TryDequeue(out LocationDefinitionModel? nxt))
            {
                if (!nxt.RewardIsFixed && (spoilerData[nxt] & flags) != ArchipelagoItemFlags.None && locationsToIgnore.Add(nxt))
                {
                    yield return nxt;
                }

                foreach (LocationDefinitionModel connected in GameDefinitions.Instance.ConnectedLocations[nxt])
                {
                    if (queued.Add(connected))
                    {
                        q.Enqueue(connected);
                    }
                }
            }
        }
    }

    public bool Equals(GameState? other)
    {
        return
            other is not null &&
            Epoch == other.Epoch &&
            PrngState == other.PrngState &&
            TotalNontrivialStepCount == other.TotalNontrivialStepCount &&
            PreviousLocation == other.PreviousLocation &&
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
            ReceivedItems.SequenceEqual(other.ReceivedItems) &&
            CheckedLocations.SequenceEqual(other.CheckedLocations) &&
            PriorityLocations.SequenceEqual(other.PriorityLocations);
    }

    public override int GetHashCode() => Epoch.GetHashCode();
}
