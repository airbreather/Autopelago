using System.Collections.Immutable;

namespace Autopelago;

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
        FoodFactor = copyFrom.FoodFactor;
        LuckFactor = copyFrom.LuckFactor;
        EnergyFactor = copyFrom.EnergyFactor;
        StyleFactor = copyFrom.StyleFactor;
        DistractionCounter = copyFrom.DistractionCounter;
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

    public required int FoodFactor { get; init; }

    public required int LuckFactor { get; init; }

    public required int EnergyFactor { get; init; }

    public required int StyleFactor { get; init; }

    public required int DistractionCounter { get; init; }

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
            FoodFactor = 0,
            LuckFactor = 0,
            EnergyFactor = 0,
            StyleFactor = 0,
            DistractionCounter = 0,
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

    public Proxy ToProxy()
    {
        return new()
        {
            Epoch = Epoch,
            TotalNontrivialStepCount = TotalNontrivialStepCount,
            CurrentLocation = CurrentLocation.Name,
            TargetLocation = TargetLocation.Name,
            ReceivedItems = [.. ReceivedItems.Select(i => i.Name)],
            CheckedLocations = [.. CheckedLocations.Select(l => l.Name)],
            FoodFactor = FoodFactor,
            LuckFactor = LuckFactor,
            EnergyFactor = EnergyFactor,
            StyleFactor = StyleFactor,
            DistractionCounter = DistractionCounter,
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
            HasConfidence == other.HasConfidence &&
            ReceivedItems.SequenceEqual(other.ReceivedItems) &&
            CheckedLocations.SequenceEqual(other.CheckedLocations);
    }

    public override int GetHashCode() => Epoch.GetHashCode();

    public sealed record Proxy
    {
        public ulong Epoch { get; init; }

        public required ulong TotalNontrivialStepCount { get; init; }

        public required string CurrentLocation { get; init; }

        public required string TargetLocation { get; init; }

        public required ImmutableArray<string> ReceivedItems { get; init; }

        public required ImmutableArray<string> CheckedLocations { get; init; }

        public required int FoodFactor { get; init; }

        public required int LuckFactor { get; init; }

        public required int EnergyFactor { get; init; }

        public required int StyleFactor { get; init; }

        public required int DistractionCounter { get; init; }

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
                FoodFactor = FoodFactor,
                LuckFactor = LuckFactor,
                EnergyFactor = EnergyFactor,
                StyleFactor = StyleFactor,
                DistractionCounter = DistractionCounter,
                HasConfidence = HasConfidence,
                LocationCheckAttemptsThisStep = LocationCheckAttemptsThisStep,
                ActionBalanceAfterPreviousStep = ActionBalanceAfterPreviousStep,
                PrngState = PrngState,
            };
        }
    }
}
