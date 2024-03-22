using System.Collections.ObjectModel;

namespace Autopelago;

public sealed record AutopelagoSettingsModel
{
    public required string Server { get; init; }

    public required ushort Port { get; init; }

    public string GameName { get; init; } = "Autopelago";

    public required AutopelagoDefaultGameSettingsModel DefaultSettings { get; init; }

    public Collection<AutopelagoPlayerSettingsModel> Slots { get; init; } = [];
}

public sealed record AutopelagoDefaultGameSettingsModel
{
    public required double MinSecondsPerGameStep { get; init; } = double.NaN;

    public required double MaxSecondsPerGameStep { get; init; } = double.NaN;
}

public sealed record AutopelagoOverriddenGameSettingsModel
{
    public double? MinSecondsPerGameStep { get; init; }

    public double? MaxSecondsPerGameStep { get; init; }
}

public sealed record AutopelagoPlayerSettingsModel
{
    public required string Name { get; init; }

    public string? Password { get; init; }

    public AutopelagoOverriddenGameSettingsModel? OverriddenSettings { get; init; }
}
