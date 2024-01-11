using System.Collections.ObjectModel;

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
    public double SecondsPerGameStep { get; init; } = double.NaN;

    public double MovementSpeedMultiplier { get; init; } = double.NaN;
}

public sealed record AutopelagoOverriddenGameSettingsModel
{
    public double? SecondsPerGameStep { get; init; }

    public double? MovementSpeedMultiplier { get; init; }
}

public sealed record AutopelagoPlayerSettingsModel
{
    public required string Name { get; init; }

    public string? Password { get; init; }

    public AutopelagoOverriddenGameSettingsModel? OverriddenSettings { get; init; }
}
