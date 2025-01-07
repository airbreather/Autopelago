using Spectre.Console.Cli;

namespace Autopelago.Infrastructure;

internal sealed class SimulateSettings : CommandSettings
{
    [CommandArgument(0, "<science-dir>")]
    public required string ScienceDir { get; init; }

    [CommandArgument(1, "<num-seeds>")]
    public required int NumSeeds { get; init; }

    [CommandArgument(2, "<num-slots-per-seed>")]
    public required int NumSlotsPerSeed { get; init; }

    [CommandArgument(3, "<num-slots-per-seed>")]
    public required int NumRunsPerSeed { get; init; }

    [CommandArgument(4, "<overall-seed>")]
    public required ulong OverallSeed { get; init; }

    [CommandArgument(5, "<victory-location>")]
    public required string VictoryLocation { get; init; }
}
