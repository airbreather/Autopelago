using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace Autopelago;

[StructLayout(LayoutKind.Auto, Pack = 1)]
public struct LocationAttemptTraceEvent
{
    private const ushort Mask3 = ((1 << 3) - 1);
    private const ushort Mask4 = ((1 << 4) - 1);
    private const ushort Mask5 = ((1 << 5) - 1);
    private const ushort Mask6 = ((1 << 6) - 1);

    public required ushort StepNumber { get; init; }

    public required LocationKey Location { get; init; }

    // store the next 3 numbers in two bytes (16 bits)
    private readonly ushort _counts;

    // 5 bits: shouldn't ever exceed 32
    public required byte AbilityCheckDC
    {
        get => (byte)(_counts & Mask5);
        init => _counts = (ushort)((_counts & ~Mask5) | (value & Mask5));
    }

    // 5 bits: up to AbilityCheckDC + 5, but when AbilityCheckDC starts getting that high, RatCount
    // will also get high enough that we will not need anything over 32.
    public required byte MercyModifier
    {
        get => (byte)((_counts >> 5) & Mask5);
        init => _counts = (ushort)((_counts & ~(Mask5 << 5)) | ((value & Mask5) << 5));
    }

    // 6 bits
    public required byte RatCount
    {
        get => (byte)((_counts >> 10) & Mask6);
        init => _counts = (ushort)((_counts & ~(Mask6 << 10)) | ((value & Mask6) << 10));
    }

    // 5 bits for D20, then some magic to use just 3 bits to store the 4 flags
    private readonly byte _d20AndFlags;
    public required byte D20
    {
        get => (byte)((_d20AndFlags >> 3) & Mask5);
        init => _d20AndFlags = (byte)((_d20AndFlags & ~(Mask5 << 3)) | ((value & Mask5) << 3));
    }

    // we can't possibly store all apparently possible combinations of 4 flags into just 3 bits.
    // fortunately for us, only a handful of combinations are both legal and distinguishable:
    // - whenever Lucky is set, Unlucky is always unset.
    // - whenever Lucky is set, Success is always set.
    // - whenever Lucky is set, it doesn't matter what the value is for Stylish.
    // - Unlucky and Stylish cancel each other out.
    // removing the illegal ones and combining the indistinguishable ones, we get down to only 7
    // combinations that we need to be able to represent, leaving one more to signal in our lookup
    // table that the combination is invalid. the lookup table represents the following:
    /*
        +===+===+===+===+=====+
        | L | U | S | ✓ | As3 |
        +===+===+===+===+=====+
        | N | N | N | N | 000 |
        +---+---+---+---+-----+
        | N | N | N | Y | 001 |
        +---+---+---+---+-----+
        | N | N | Y | N | 010 |
        +---+---+---+---+-----+
        | N | N | Y | Y | 011 |
        +---+---+---+---+-----+
        | N | Y | N | N | 100 |
        +---+---+---+---+-----+
        | N | Y | N | Y | 101 |
        +---+---+---+---+-----+
        | N | Y | Y | N | 000 |
        +---+---+---+---+-----+
        | N | Y | Y | Y | 001 |
        +---+---+---+---+-----+
        | Y | N | N | N | N/A |
        +---+---+---+---+-----+
        | Y | N | N | Y | 110 |
        +---+---+---+---+-----+
        | Y | N | Y | N | N/A |
        +---+---+---+---+-----+
        | Y | N | Y | Y | 110 |
        +---+---+---+---+-----+
        | Y | Y | N | N | N/A |
        +---+---+---+---+-----+
        | Y | Y | N | Y | N/A |
        +---+---+---+---+-----+
        | Y | Y | Y | N | N/A |
        +---+---+---+---+-----+
        | Y | Y | Y | Y | N/A |
        +---+---+---+---+-----+
    */
    private static readonly ImmutableArray<byte> s_flagsLookup = [0, 1, 2, 3, 4, 5, 0, 1, 7, 6, 7, 6, 7, 7, 7, 7];

    public required byte CombinedFlags
    {
        init => _d20AndFlags = (byte)((_d20AndFlags & ~Mask3) | (s_flagsLookup[value & Mask4] switch
        {
            7 => throw new ArgumentException("Invalid combination of flags", nameof(value)),
            byte val => val,
        }));
    }

    public bool HasLucky => (_d20AndFlags & Mask3) is 6;

    public bool HasUnlucky => (_d20AndFlags & Mask3) is 4 or 5;

    public bool HasStylish => (_d20AndFlags & Mask3) is 2 or 3;

    public bool Success => (_d20AndFlags & Mask3) is 1 or 3 or 5 or 6;

    public static byte ToCombinedFlags(bool hasLucky, bool hasUnlucky, bool hasStylish, bool success)
    {
        return (byte)(
            ((hasLucky ? 1 : 0) << 3) |
            ((hasUnlucky ? 1 : 0) << 2) |
            ((hasStylish ? 1 : 0) << 1) |
            ((success ? 1 : 0) << 0
        ));
    }
}

public sealed class GameInstrumentation : IDisposable
{
    private static readonly ConcurrentBag<List<LocationAttemptTraceEvent>> s_pool = [];

    private readonly List<LocationAttemptTraceEvent> _locationAttempts = s_pool.TryTake(out List<LocationAttemptTraceEvent>? locationAttempts) ? locationAttempts : [];

    private int _stepNumber;

    public GameInstrumentation()
    {
        Attempts = _locationAttempts.AsReadOnly();
    }

    public void Dispose()
    {
        _locationAttempts.Clear();
        s_pool.Add(_locationAttempts);
    }

    public int StepNumber => _stepNumber;

    public ReadOnlyCollection<LocationAttemptTraceEvent> Attempts { get; }

    public void NextStep()
    {
        _stepNumber++;
    }

    public void TraceLocationAttempt(LocationKey location, byte roll, bool hasLucky, bool hasUnlucky, bool hasStylish, byte ratCount, byte abilityCheckDC, byte mercyModifier, bool success)
    {
        _locationAttempts.Add(new()
        {
            StepNumber = (ushort)_stepNumber,
            Location = location,
            D20 = roll,
            RatCount = ratCount,
            AbilityCheckDC = abilityCheckDC,
            MercyModifier = mercyModifier,
            CombinedFlags = LocationAttemptTraceEvent.ToCombinedFlags(
                hasLucky: hasLucky,
                hasUnlucky: hasUnlucky,
                hasStylish: hasStylish,
                success: success),
        });
    }
}
