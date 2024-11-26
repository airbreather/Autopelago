using System.Collections.ObjectModel;

namespace Autopelago;

public sealed record MovementTraceEvent
{
    public required int StepNumber { get; init; }

    public required LocationDefinitionModel From { get; init; }

    public required LocationDefinitionModel To { get; init; }

    public required TargetLocationReason Reason { get; init; }
}

public sealed record LocationAttemptTraceEvent
{
    public required int StepNumber { get; init; }

    public required LocationDefinitionModel Location { get; init; }

    private readonly byte _rollFlags;

    public required byte Roll
    {
        get => (byte)(_rollFlags & ((1 << 5) - 1));
        init => _rollFlags |= value;
    }

    public required bool HasLucky
    {
        get => (_rollFlags & (1 << 5)) != 0;
        init => _rollFlags = value
            ? (byte)(_rollFlags | (1 << 5))
            : (byte)(_rollFlags & ~(1 << 5));
    }

    public required bool HasUnlucky
    {
        get => (_rollFlags & (1 << 6)) != 0;
        init => _rollFlags = value
            ? (byte)(_rollFlags | (1 << 6))
            : (byte)(_rollFlags & ~(1 << 6));
    }

    public required bool HasStylish
    {
        get => (_rollFlags & (1 << 7)) != 0;
        init => _rollFlags = value
            ? (byte)(_rollFlags | (1 << 7))
            : (byte)(_rollFlags & ~(1 << 7));
    }

    public required byte RatCount { get; init; }

    public required byte AbilityCheckDC { get; init; }
}

public sealed class GameInstrumentation
{
    private readonly List<MovementTraceEvent> _movements = [];

    private readonly List<LocationAttemptTraceEvent> _locationAttempts = [];

    private int _stepNumber;

    public GameInstrumentation()
    {
        Movements = _movements.AsReadOnly();
        LocationAttempts = _locationAttempts.AsReadOnly();
    }

    public int StepNumber => _stepNumber;

    public ReadOnlyCollection<MovementTraceEvent> Movements { get; }

    public ReadOnlyCollection<LocationAttemptTraceEvent> LocationAttempts { get; }

    public void NextStep()
    {
        _stepNumber++;
    }

    public void TraceMovement(LocationDefinitionModel from, LocationDefinitionModel to, TargetLocationReason reason)
    {
        _movements.Add(new()
        {
            StepNumber = _stepNumber,
            From = from,
            To = to,
            Reason = reason,
        });
    }

    public void TraceLocationAttempt(LocationDefinitionModel location, byte roll, bool hasLucky, bool hasUnlucky, bool hasStylish, byte ratCount, byte abilityCheckDC)
    {
        _locationAttempts.Add(new()
        {
            StepNumber = _stepNumber,
            Location = location,
            Roll = roll,
            HasLucky = hasLucky,
            HasUnlucky = hasUnlucky,
            HasStylish = hasStylish,
            RatCount = ratCount,
            AbilityCheckDC = abilityCheckDC,
        });
    }
}
