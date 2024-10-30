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
        Startled,
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

    public GameState AddStartled(int toAdd)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(toAdd, 0);
        if (toAdd == 0)
        {
            return this;
        }

        int newStartledCounter = StartledCounter + toAdd;
        LocationDefinitionModel newTargetLocation = TargetLocation;
        ImmutableList<PriorityLocationModel> newPriorityLocations = PriorityLocations;
        if (newStartledCounter == toAdd)
        {
            // the rat wasn't startled, and now it is.
            newPriorityLocations = newPriorityLocations.Insert(0, new()
            {
                Location = newTargetLocation = CurrentLocation.NextLocationTowards(GameDefinitions.Instance.StartLocation, this),
                Source = PriorityLocationModel.SourceKind.Startled,
            });
        }

        return this with
        {
            TargetLocation = newTargetLocation,
            StartledCounter = newStartledCounter,
            PriorityLocations = newPriorityLocations,
        };
    }

    public GameStateProxy ToProxy()
    {
        return new()
        {
            Epoch = Epoch,
            TotalNontrivialStepCount = TotalNontrivialStepCount,
            CurrentLocation = CurrentLocation.Name,
            TargetLocation = TargetLocation.Name,
            ReceivedItems = [.. ReceivedItems.Select(i => i.Name)],
            CheckedLocations = [.. CheckedLocations.Select(l => l.Name)],
            PriorityLocations = [.. PriorityLocations.Select(l => l.ToProxy())],
            FoodFactor = FoodFactor,
            LuckFactor = LuckFactor,
            EnergyFactor = EnergyFactor,
            StyleFactor = StyleFactor,
            DistractionCounter = DistractionCounter,
            StartledCounter = StartledCounter,
            HasConfidence = HasConfidence,
            LocationCheckAttemptsThisStep = LocationCheckAttemptsThisStep,
            ActionBalanceAfterPreviousStep = ActionBalanceAfterPreviousStep,
            PrngState = PrngState,
        };
    }

    public bool Equals(GameState? other)
    {
        return
            other is not null &&
            Epoch == other.Epoch &&
            PrngState == other.PrngState &&
            TotalNontrivialStepCount == other.TotalNontrivialStepCount &&
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

    public sealed record GameStateProxy
    {
        public ulong Epoch { get; init; }

        public required ulong TotalNontrivialStepCount { get; init; }

        public required string CurrentLocation { get; init; }

        public required string TargetLocation { get; init; }

        public required ImmutableArray<string> ReceivedItems { get; init; }

        public required ImmutableArray<string> CheckedLocations { get; init; }

        public required ImmutableArray<PriorityLocationModel.PriorityLocationModelProxy> PriorityLocations { get; init; }

        public required int FoodFactor { get; init; }

        public required int LuckFactor { get; init; }

        public required int EnergyFactor { get; init; }

        public required int StyleFactor { get; init; }

        public required int DistractionCounter { get; init; }

        public required int StartledCounter { get; init; }

        public required bool HasConfidence { get; init; }

        public required int LocationCheckAttemptsThisStep { get; init; }

        public required int ActionBalanceAfterPreviousStep { get; init; }

        public Prng.State PrngState { get; init; }

        public GameState ToState()
        {
            return new()
            {
                Epoch = Epoch,
                TotalNontrivialStepCount = TotalNontrivialStepCount,
                CurrentLocation = GameDefinitions.Instance.LocationsByName[CurrentLocation],
                TargetLocation = GameDefinitions.Instance.LocationsByName[TargetLocation],
                ReceivedItems = [.. ReceivedItems.Select(name => GameDefinitions.Instance.ItemsByName[name])],
                CheckedLocations = [.. CheckedLocations.Select(name => GameDefinitions.Instance.LocationsByName[name])],
                PriorityLocations = [.. PriorityLocations.Select(loc => loc.ToPriorityLocation())],
                FoodFactor = FoodFactor,
                LuckFactor = LuckFactor,
                EnergyFactor = EnergyFactor,
                StyleFactor = StyleFactor,
                DistractionCounter = DistractionCounter,
                StartledCounter = StartledCounter,
                HasConfidence = HasConfidence,
                LocationCheckAttemptsThisStep = LocationCheckAttemptsThisStep,
                ActionBalanceAfterPreviousStep = ActionBalanceAfterPreviousStep,
                PrngState = PrngState,
            };
        }
    }
}
