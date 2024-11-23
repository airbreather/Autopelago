using System.Collections.Immutable;

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

public sealed record GameState
{
    private GameState()
    {
    }

    public required LocationDefinitionModel CurrentLocation { get; init; }

    public required LocationDefinitionModel TargetLocation { get; init; }

    public required ReceivedItems ReceivedItems { get; init; }

    public required CheckedLocations CheckedLocations { get; init; }

    public required ImmutableList<LocationDefinitionModel> PriorityLocations { get; init; }

    public required int FoodFactor { get; init; }

    public required int LuckFactor { get; init; }

    public required int EnergyFactor { get; init; }

    public required int StyleFactor { get; init; }

    public required int DistractionCounter { get; init; }

    public required int StartledCounter { get; init; }

    public required bool HasConfidence { get; init; }

    public required Prng.State PrngState { get; init; }

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
            CurrentLocation = GameDefinitions.Instance.StartLocation,
            TargetLocation = GameDefinitions.Instance.StartLocation,
            ReceivedItems = new() { InReceivedOrder = [] },
            CheckedLocations = new() { InCheckedOrder = [] },
            PriorityLocations = [],
            FoodFactor = 0,
            LuckFactor = 0,
            EnergyFactor = 0,
            StyleFactor = 0,
            DistractionCounter = 0,
            StartledCounter = 0,
            HasConfidence = false,
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
}
