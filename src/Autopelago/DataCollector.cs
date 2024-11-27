using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using Autopelago.BespokeMultiworld;

using Serilog;

namespace Autopelago;

public static class DataCollector
{
    private static readonly FileStreamOptions s_create = new()
    {
        Mode = FileMode.Create,
        Access = FileAccess.ReadWrite,
        Share = FileShare.Read,
        Options = FileOptions.Asynchronous,
    };

    public static async Task RunAsync(string scienceDir, int numSeeds, int numSlotsPerSeed, int numRunsPerSeed, Prng.State seed, CancellationToken cancellationToken)
    {
        scienceDir = scienceDir.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Directory.CreateDirectory(Path.GetDirectoryName(PlaythroughGenerator.Paths.ResultFileForMovements(scienceDir))!);
        await using StreamWriter outMovements = new(PlaythroughGenerator.Paths.ResultFileForMovements(scienceDir), Encoding.UTF8, s_create);
        Directory.CreateDirectory(Path.GetDirectoryName(PlaythroughGenerator.Paths.ResultFileForLocationAttempts(scienceDir))!);
        await using StreamWriter outLocationAttempts = new(PlaythroughGenerator.Paths.ResultFileForLocationAttempts(scienceDir), Encoding.UTF8, s_create);

        Prng.State state = seed;
        Prng.State[] multiworldSeeds = new Prng.State[numSeeds];
        ImmutableArray<FrozenDictionary<LocationKey, WorldItem>>[] allSpoilerData = new ImmutableArray<FrozenDictionary<LocationKey, WorldItem>>[multiworldSeeds.Length];
        ImmutableArray<FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>>>[] partialSpoilerData = new ImmutableArray<FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>>>[multiworldSeeds.Length];
        for (int i = 0; i < multiworldSeeds.Length; i++)
        {
            multiworldSeeds[i] = state;
            Prng.LongJump(ref state);
        }

        await Parallel.ForAsync(0, multiworldSeeds.Length, cancellationToken, async (i, cancellationToken2) =>
        {
            UInt128 archipelagoSeed = new(Prng.Next(ref multiworldSeeds[i]), Prng.Next(ref multiworldSeeds[i]));
            allSpoilerData[i] = await PlaythroughGenerator.GenerateAsync(scienceDir, archipelagoSeed, numSlotsPerSeed, cancellationToken2);
            partialSpoilerData[i] = ImmutableArray.CreateRange(allSpoilerData[i], val => val
                .GroupBy(kvp => kvp.Value.Item.ArchipelagoFlags, kvp => kvp.Key)
                .ToFrozenDictionary(grp => grp.Key, grp => grp.ToFrozenSet()));
        });

        ImmutableArray<LocationAttemptTraceEvent>[] locationAttempts = new ImmutableArray<LocationAttemptTraceEvent>[checked(numSeeds * numSlotsPerSeed * numRunsPerSeed)];
        ImmutableArray<MovementTraceEvent>[] movements = new ImmutableArray<MovementTraceEvent>[locationAttempts.Length];
        Lock lck = new();
        int done = 0;
        int maxLineLength = 0;
        long lastReport = Stopwatch.GetTimestamp();
        TimeSpan reportInterval = TimeSpan.FromMilliseconds(250);
        long startTimestamp = Stopwatch.GetTimestamp();
        Parallel.For(0, numSeeds * numRunsPerSeed, new ParallelOptions { CancellationToken = cancellationToken }, (ij, loopState) =>
        {
            if (loopState.ShouldExitCurrentIteration)
            {
                return;
            }

            try
            {
                int i = Math.DivRem(ij, numRunsPerSeed, out int j);
                Prng.State multiworldPrngState = multiworldSeeds[i];
                World[] slotsMutable = new World[allSpoilerData[i].Length];
                for (int k = 0; k < numSlotsPerSeed; k++)
                {
                    slotsMutable[k] = new(multiworldPrngState);
                }

                ImmutableArray<World> slots = ImmutableCollectionsMarshal.AsImmutableArray(slotsMutable);
                Multiworld multiworld = new() { Slots = slots, FullSpoilerData = allSpoilerData[i], PartialSpoilerData = partialSpoilerData[i], };

                multiworld.Run();
                for (int k = 0; k < numSlotsPerSeed; k++)
                {
                    locationAttempts[(i * numRunsPerSeed * numSlotsPerSeed) + (j * numSlotsPerSeed) + k] = [.. slots[k].Instrumentation.LocationAttempts];
                    movements[(i * numRunsPerSeed * numSlotsPerSeed) + (j * numSlotsPerSeed) + k] = [.. slots[k].Instrumentation.Movements];
                }

                int doneHere = Interlocked.Increment(ref done);
                if (Stopwatch.GetElapsedTime(lastReport) > reportInterval)
                {
                    lock (lck)
                    {
                        if (Stopwatch.GetElapsedTime(lastReport) > reportInterval)
                        {
                            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                            TimeSpan estimatedRemaining = ((numSeeds * numRunsPerSeed) - doneHere) * (elapsed / doneHere);
                            string message = $"\rFinished #{doneHere} after {elapsed.FormatMyWay()}, meaning about {estimatedRemaining.FormatMyWay()} left".PadRight(maxLineLength);
                            maxLineLength = Math.Max(maxLineLength, message.Length);
                            Console.Write(message);
                            lastReport = Stopwatch.GetTimestamp();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                loopState.Stop();
                Log.Fatal(ex, "Error occurred while trying to run.");
            }
        });

        if (done > 0)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            Console.WriteLine($"\rFinished #{done} after {elapsed.FormatMyWay()}".PadRight(maxLineLength));
        }

        await outMovements.WriteLineAsync("SeedNumber,IterationNumber,SlotNumber,StepNumber,FromRegion,FromN,ToRegion,ToN,Reason");
        await outLocationAttempts.WriteLineAsync("SeedNumber,IterationNumber,SlotNumber,StepNumber,Region,N,AbilityCheckDC,Roll,RatCount,Auras");
        for (int i = 0; i < numSeeds; i++)
        {
            for (int j = 0; j < numRunsPerSeed; j++)
            {
                for (int k = 0; k < numSlotsPerSeed; k++)
                {
                    foreach (MovementTraceEvent movement in movements[(i * numRunsPerSeed * numSlotsPerSeed) + (j * numSlotsPerSeed) + k])
                    {
                        await outMovements.WriteLineAsync($"{i},{j},{k},{movement.StepNumber},{movement.From.Key.RegionKey},{movement.From.Key.N},{movement.To.Key.RegionKey},{movement.To.Key.N},{(byte)movement.Reason}");
                    }

                    foreach (LocationAttemptTraceEvent locationAttempt in locationAttempts[(i * numRunsPerSeed * numSlotsPerSeed) + (j * numSlotsPerSeed) + k])
                    {
                        int auras =
                            (locationAttempt.HasLucky ? 1 : 0) << 0 |
                            (locationAttempt.HasUnlucky ? 1 : 0) << 1 |
                            (locationAttempt.HasStylish ? 1 : 0) << 2;
                        await outLocationAttempts.WriteLineAsync($"{i},{j},{k},{locationAttempt.StepNumber},{locationAttempt.Location.Key.RegionKey},{locationAttempt.Location.Key.N},{locationAttempt.AbilityCheckDC},{locationAttempt.Roll},{locationAttempt.RatCount},{auras}");
                    }
                }
            }
        }
    }
}
