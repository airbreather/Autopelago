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

using Spectre.Console.Cli;

using ZstdSharp;
using ZstdSharp.Unsafe;

namespace Autopelago.Infrastructure;

[JsonSerializable(typeof(Prng.State))]
public sealed partial class PrngStateSerializerContext : JsonSerializerContext;

internal sealed class SimulateCommand : CancelableAsyncCommand<SimulateSettings>
{
    private static readonly FileStreamOptions s_create = new()
    {
        Mode = FileMode.Create,
        Access = FileAccess.ReadWrite,
        Share = FileShare.Read,
        Options = FileOptions.Asynchronous,
    };

    public override async Task<int> ExecuteAsync(CommandContext context, SimulateSettings settings, CancellationToken cancellationToken)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .Enrich.FromLogContext()
            .CreateLogger();

        string scienceDir = settings.ScienceDir.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Compressor compressor = new(10);
        compressor.SetParameter(ZSTD_cParameter.ZSTD_c_nbWorkers, Environment.ProcessorCount);
        Directory.CreateDirectory(Path.GetDirectoryName(PlaythroughGenerator.Paths.ResultFile(scienceDir))!);
        await using StreamWriter outLocationAttempts = new(new CompressionStream(new FileStream(PlaythroughGenerator.Paths.ResultFile(scienceDir), s_create), compressor, preserveCompressor: false, leaveOpen: false), Encoding.UTF8);

        Prng.State state = Prng.State.Start(settings.OverallSeed);
        Prng.State[] multiworldSeeds = new Prng.State[settings.NumSeeds];
        ImmutableArray<ImmutableArray<WorldItem>>[] allSpoilerData = new ImmutableArray<ImmutableArray<WorldItem>>[multiworldSeeds.Length];
        ImmutableArray<FrozenDictionary<ArchipelagoItemFlags, BitArray384>>[] partialSpoilerData = new ImmutableArray<FrozenDictionary<ArchipelagoItemFlags, BitArray384>>[multiworldSeeds.Length];
        for (int i = 0; i < multiworldSeeds.Length; i++)
        {
            multiworldSeeds[i] = state;
            Prng.LongJump(ref state);
            Log.Information("Seed #{SeedNum} = {Seed}", i, JsonSerializer.Serialize(multiworldSeeds[i], PrngStateSerializerContext.Default.State));
        }

        await Parallel.ForAsync(0, multiworldSeeds.Length, cancellationToken, async (i, cancellationToken2) =>
        {
            UInt128 archipelagoSeed = new(Prng.Next(ref multiworldSeeds[i]), Prng.Next(ref multiworldSeeds[i]));
            allSpoilerData[i] = await PlaythroughGenerator.GenerateAsync(scienceDir, archipelagoSeed, settings.NumSlotsPerSeed, cancellationToken2);
            partialSpoilerData[i] = ImmutableArray.CreateRange(allSpoilerData[i], val => val
                .Select((item, j) => KeyValuePair.Create(new LocationKey { N = j }, item))
                .GroupBy(kvp => GameDefinitions.Instance[kvp.Value.Item].ArchipelagoFlags, kvp => kvp.Key)
                .ToFrozenDictionary(grp => grp.Key, ToSpoilerData));

            BitArray384 ToSpoilerData(IEnumerable<LocationKey> locations)
            {
                BitArray384 spoilerData = new(GameDefinitions.Instance.AllLocations.Length);
                foreach (LocationKey location in locations)
                {
                    spoilerData[location.N] = true;
                }

                return spoilerData;
            }
        });

        ImmutableArray<LocationAttemptTraceEvent>[] locationAttempts = new ImmutableArray<LocationAttemptTraceEvent>[checked(settings.NumSeeds * settings.NumSlotsPerSeed * settings.NumRunsPerSeed)];
        TimeSpan reportInterval = TimeSpan.FromMilliseconds(400);
        long startTimestamp = Stopwatch.GetTimestamp();
        int[] completed = new int[settings.NumSeeds * 1024];
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
                    BitArray remaining = new(settings.NumSeeds, true);
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
                                done += settings.NumRunsPerSeed;
                                continue;
                            }

                            int cmp = Volatile.Read(in completed[i * 1024]);
                            done += cmp;
                            if (cmp < settings.NumRunsPerSeed)
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
                            ? ((settings.NumSeeds * settings.NumRunsPerSeed) - done) * (elapsed / done)
                            : TimeSpan.FromHours(10);
                        remainingSeedsMessage.Length = Math.Max(0, remainingSeedsMessage.Length - 2);
                        string msg = $"\r{elapsed.FormatMyWay()}, done {done}, rem {estimatedRemaining.FormatMyWay()} to finish: {remainingSeedsMessage}";
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
        Parallel.For(0, settings.NumSeeds * settings.NumRunsPerSeed, new() { CancellationToken = cancellationToken }, (ij, loopState) =>
        {
            if (loopState.ShouldExitCurrentIteration)
            {
                return;
            }

            try
            {
                int i = Math.DivRem(ij, settings.NumRunsPerSeed, out int j);
                Prng.State multiworldPrngState = multiworldSeeds[i];
                World[] slotsMutable = new World[allSpoilerData[i].Length];
                for (int k = 0; k < settings.NumSlotsPerSeed; k++)
                {
                    slotsMutable[k] = new(multiworldPrngState);
                }

                ImmutableArray<World> slots = ImmutableCollectionsMarshal.AsImmutableArray(slotsMutable);
                using Multiworld multiworld = new() { Slots = slots, FullSpoilerData = allSpoilerData[i], PartialSpoilerData = partialSpoilerData[i], };

                multiworld.Run();
                for (int k = 0; k < settings.NumSlotsPerSeed; k++)
                {
                    locationAttempts[(i * settings.NumRunsPerSeed * settings.NumSlotsPerSeed) + (j * settings.NumSlotsPerSeed) + k] = [.. slots[k].Instrumentation.Attempts];
                }

                Interlocked.Increment(ref completed[i * 1024]);
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

        StringBuilder sb = new("SeedNumber,IterationNumber,SlotNumber,StepNumber,Region,N,AbilityCheckDC,RatCount,MercyModifier,HasLucky,HasUnlucky,HasStylish,Roll,Success");
        await outLocationAttempts.WriteLineAsync(sb, cancellationToken);
        for (int i = 0; i < settings.NumSeeds; i++)
        {
            for (int j = 0; j < settings.NumRunsPerSeed; j++)
            {
                for (int k = 0; k < settings.NumSlotsPerSeed; k++)
                {
                    foreach (LocationAttemptTraceEvent l in locationAttempts[(i * settings.NumRunsPerSeed * settings.NumSlotsPerSeed) + (j * settings.NumSlotsPerSeed) + k])
                    {
                        RegionLocationKey regionLocation = GameDefinitions.Instance[l.Location].RegionLocationKey;
                        sb.Clear();
                        sb.Append($"{i},{j},{k},{l.StepNumber},{GameDefinitions.Instance.Region[regionLocation].YamlKey},{regionLocation.N},{l.AbilityCheckDC},{l.RatCount},{l.MercyModifier},{(l.HasLucky ? 1 : 0)},{(l.HasUnlucky ? 1 : 0)},{(l.HasStylish ? 1 : 0)},{l.D20},{(l.Success ? 1 : 0)}");
                        await outLocationAttempts.WriteLineAsync(sb, cancellationToken);
                    }
                }
            }
        }

        return 0;
    }
}
