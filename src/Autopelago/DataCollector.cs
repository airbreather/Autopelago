using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Autopelago.BespokeMultiworld;

namespace Autopelago;

[StructLayout(LayoutKind.Auto)]
public readonly record struct GameEvent
{
    public required GameEventId Id { get; init; }

    private readonly byte _seedNumber;
    public required int SeedNumber
    {
        get => _seedNumber;
        init => _seedNumber = checked((byte)value);
    }

    private readonly short _iterationNumber;
    public required int IterationNumber
    {
        get => _iterationNumber;
        init => _iterationNumber = checked((short)value);
    }

    private readonly byte _slotNumber;
    public required int SlotNumber
    {
        get => _slotNumber;
        init => _slotNumber = checked((byte)value);
    }
}

public static class DataCollector
{
    public static async Task RunAsync(Prng.State seed, CancellationToken cancellationToken)
    {
        Prng.State state = seed;
        Prng.State[] multiworldSeeds = new Prng.State[250];
        ImmutableArray<FrozenDictionary<LocationKey, WorldItem>>[] allSpoilerData = new ImmutableArray<FrozenDictionary<LocationKey, WorldItem>>[250];
        ImmutableArray<FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>>>[] partialSpoilerData = new ImmutableArray<FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>>>[250];
        for (int i = 0; i < multiworldSeeds.Length; i++)
        {
            multiworldSeeds[i] = state;
            Prng.LongJump(ref state);
        }

        await Parallel.ForAsync(0, multiworldSeeds.Length, cancellationToken, async (i, cancellationToken2) =>
        {
            UInt128 archipelagoSeed = new(Prng.Next(ref multiworldSeeds[i]), Prng.Next(ref multiworldSeeds[i]));
            allSpoilerData[i] = await PlaythroughGenerator.GenerateAsync(archipelagoSeed, cancellationToken2);
            partialSpoilerData[i] = ImmutableArray.CreateRange(allSpoilerData[i], val => val
                .GroupBy(kvp => kvp.Value.Item.ArchipelagoFlags, kvp => kvp.Key)
                .ToFrozenDictionary(grp => grp.Key, grp => grp.ToFrozenSet()));
        });

        int done = 0;
        long lastReport = Stopwatch.GetTimestamp();
        TimeSpan reportInterval = TimeSpan.FromMilliseconds(50);
        Lock l = new();
        Parallel.For(0, 250_000, (ij, loopState) =>
        {
            if (loopState.IsStopped)
            {
                return;
            }

            try
            {
                int i = Math.DivRem(ij, 1000, out int j);

                Prng.State multiworldPrngState = multiworldSeeds[i];
                World[] slotsMutable = new World[allSpoilerData[i].Length];
                int gameEvents = 0;
                for (int k = 0; k < 4; k++)
                {
                    slotsMutable[k] = new(multiworldPrngState);
                    slotsMutable[k].Instrumentation.GameEvents.Subscribe(_ => gameEvents++);
                }

                ImmutableArray<World> slots = ImmutableCollectionsMarshal.AsImmutableArray(slotsMutable);
                using Multiworld multiworld = new()
                {
                    Slots = slots,
                    FullSpoilerData = allSpoilerData[i],
                    PartialSpoilerData = partialSpoilerData[i],
                };

                multiworld.Run();
                Interlocked.Increment(ref done);
                if (Stopwatch.GetElapsedTime(lastReport) > reportInterval)
                {
                    lock (l)
                    {
                        if (Stopwatch.GetElapsedTime(lastReport) > reportInterval)
                        {
                            Console.Write($"\rFinished #{done} with {gameEvents} events");
                            lastReport = Stopwatch.GetTimestamp();
                        }
                    }
                }
            }
            catch
            {
                loopState.Stop();
                throw;
            }
        });
    }
}
