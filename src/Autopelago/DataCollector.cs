using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Autopelago.BespokeMultiworld;

namespace Autopelago;

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

        ImmutableArray<LocationAttemptTraceEvent>[] locationAttempts = new ImmutableArray<LocationAttemptTraceEvent>[1_000_000];
        ImmutableArray<MovementTraceEvent>[] movements = new ImmutableArray<MovementTraceEvent>[1_000_000];
        int done = 0;
        long startTimestamp = Stopwatch.GetTimestamp();
        Parallel.For(0, 250_000, (ij, loopState) =>
        {
            if (loopState.ShouldExitCurrentIteration)
            {
                return;
            }

            try
            {
                int i = Math.DivRem(ij, 1000, out int j);

                Prng.State multiworldPrngState = multiworldSeeds[i];
                World[] slotsMutable = new World[allSpoilerData[i].Length];
                for (int k = 0; k < 4; k++)
                {
                    slotsMutable[k] = new(multiworldPrngState);
                }

                ImmutableArray<World> slots = ImmutableCollectionsMarshal.AsImmutableArray(slotsMutable);
                Multiworld multiworld = new()
                {
                    Slots = slots,
                    FullSpoilerData = allSpoilerData[i],
                    PartialSpoilerData = partialSpoilerData[i],
                };

                multiworld.Run();
                for (int k = 0; k < 4; k++)
                {
                    locationAttempts[(ij * 4) + k] = [.. slots[k].Instrumentation.LocationAttempts];
                    movements[(ij * 4) + k] = [.. slots[k].Instrumentation.Movements];
                }

                int doneHere = Interlocked.Increment(ref done);
                if ((doneHere & 127) == 0)
                {
                    TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                    TimeSpan estimatedRemaining = (250_000 - doneHere) * (elapsed / doneHere);
                    Console.Write($"\rFinished #{doneHere} after {elapsed:m\\:ss}, meaning about {estimatedRemaining:m\\:ss} left");
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
