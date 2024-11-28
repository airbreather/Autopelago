using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Autopelago.BespokeMultiworld;

using Serilog;

namespace Autopelago;

[JsonSerializable(typeof(Prng.State))]
public sealed partial class PrngStateSerializerContext : JsonSerializerContext;

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
            Log.Information("Seed #{SeedNum} = {Seed}", i, JsonSerializer.Serialize(multiworldSeeds[i], PrngStateSerializerContext.Default.State));
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
        TimeSpan reportInterval = TimeSpan.FromMilliseconds(400);
        long startTimestamp = Stopwatch.GetTimestamp();
        int[] completed = new int[numSeeds];
        CancellationTokenSource cancelReports = new();
        Task reportTask = Task.Run(() =>
        {
            TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            new Thread(RunReport) { IsBackground = true, Priority = ThreadPriority.Highest }.Start();
            return tcs.Task;
            void RunReport()
            {
                try
                {
                    BitArray remaining = new(numSeeds, true);
                    int lastLineLength = 0;
                    StringBuilder remainingSeedsMessage = new();
                    while (!cancelReports.IsCancellationRequested)
                    {
                        Thread.Sleep(reportInterval);
                        int done = 0;
                        int reportedNum = 0;
                        for (int i = 0; i < remaining.Length; i++)
                        {
                            if (!remaining[i])
                            {
                                done += numRunsPerSeed;
                                continue;
                            }

                            int cmp = Volatile.Read(in completed[i]);
                            done += cmp;
                            if (cmp < numRunsPerSeed)
                            {
                                if (++reportedNum < 10)
                                {
                                    remainingSeedsMessage.Append($"#{i}, ");
                                }

                                if (reportedNum == 10)
                                {
                                    remainingSeedsMessage.Append("(more?)...  ");
                                }

                                continue;
                            }

                            if (lastLineLength > 0)
                            {
                                Console.Write("\r".PadRight(lastLineLength));
                                Console.CursorLeft = 0;
                            }

                            Log.Information("Seed #{Num} is complete.", i);
                            lastLineLength = 0;
                            remaining[i] = false;
                        }

                        TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                        TimeSpan estimatedRemaining = done > 0
                            ? ((numSeeds * numRunsPerSeed) - done) * (elapsed / done)
                            : TimeSpan.FromHours(10);
                        remainingSeedsMessage.Length = Math.Max(0, remainingSeedsMessage.Length - 2);
                        string msg = $"\rFinished {done} run(s) after {elapsed.FormatMyWay()}, meaning about {estimatedRemaining.FormatMyWay()} left. Seeds remaining: {remainingSeedsMessage}";
                        remainingSeedsMessage.Clear();
                        Console.Write(msg.PadRight(lastLineLength));
                        lastLineLength = msg.Length;
                    }

                    tcs.TrySetResult();
                }
                catch (OperationCanceledException ex)
                {
                    tcs.TrySetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }
        }, cancelReports.Token);
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

                Interlocked.Increment(ref completed[i]);
            }
            catch (Exception ex)
            {
                loopState.Stop();
                Log.Fatal(ex, "Error occurred while trying to run.");
            }
        });

        await cancelReports.CancelAsync();
        try
        {
            await reportTask;
        }
        catch (OperationCanceledException)
        {
        }

        await outMovements.WriteLineAsync("SeedNumber,IterationNumber,SlotNumber,StepNumber,FromRegion,FromN,ToRegion,ToN,Reason");
        await outLocationAttempts.WriteLineAsync("SeedNumber,IterationNumber,SlotNumber,StepNumber,Region,N,AbilityCheckDC,MercyModifier,Roll,RatCount,Auras");
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
                        await outLocationAttempts.WriteLineAsync($"{i},{j},{k},{locationAttempt.StepNumber},{locationAttempt.Location.Key.RegionKey},{locationAttempt.Location.Key.N},{locationAttempt.AbilityCheckDC},{locationAttempt.MercyModifier},{locationAttempt.Roll},{locationAttempt.RatCount},{auras}");
                    }
                }
            }
        }
    }
}