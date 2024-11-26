using System.Collections.Frozen;
using System.Collections.Immutable;
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
        for (int i = 0; i < multiworldSeeds.Length; i++)
        {
            multiworldSeeds[i] = state;
            Prng.LongJump(ref state);
        }

        await Parallel.ForAsync(0, multiworldSeeds.Length, cancellationToken, async (i, cancellationToken2) =>
        {
            UInt128 archipelagoSeed = new(Prng.Next(ref multiworldSeeds[i]), Prng.Next(ref multiworldSeeds[i]));
            allSpoilerData[i] = await PlaythroughGenerator.GenerateAsync(archipelagoSeed, cancellationToken2);
        });

        int done = 0;
        Parallel.For(0, multiworldSeeds.Length, i =>
        {
            Prng.State multiworldPrngState = multiworldSeeds[i];
            int seedNumber = i;
            ImmutableArray<FrozenDictionary<LocationKey, WorldItem>> fullSpoilerData = allSpoilerData[i];
            Parallel.For(0, 1000, j =>
            {
                List<GameEvent> gameEvents = [];
                int iterationNumber = j;
                World[] slotsMutable = new World[fullSpoilerData.Length];
                for (int k = 0; k < slotsMutable.Length; k++)
                {
                    int slotNumber = k;
                    slotsMutable[k] = new(multiworldPrngState);
                    slotsMutable[k].Instrumentation.GameEvents.Subscribe(id => gameEvents.Add(new()
                    {
                        Id = id,
                        SeedNumber = seedNumber,
                        IterationNumber = iterationNumber,
                        SlotNumber = slotNumber,
                    }));
                    Prng.ShortJump(ref multiworldPrngState);
                }

                ImmutableArray<World> slots = ImmutableCollectionsMarshal.AsImmutableArray(slotsMutable);
                using Multiworld multiworld = new()
                {
                    Slots = slots,
                    FullSpoilerData = fullSpoilerData,
                };

                multiworld.Run();
                Console.Write($"\rFinished #{Interlocked.Increment(ref done)} with {gameEvents.Count} events");

                gameEvents.Clear();
            });
        });
    }
}
