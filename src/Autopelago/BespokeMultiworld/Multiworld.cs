using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Autopelago.BespokeMultiworld;

public sealed class Multiworld : IDisposable
{
    public required ImmutableArray<World> Slots { get; init; }

    public required ImmutableArray<FrozenDictionary<LocationKey, WorldItem>> FullSpoilerData { get; init; }

    public required ImmutableArray<FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>>> PartialSpoilerData { get; init; }

    public void Dispose()
    {
        foreach (World slot in Slots)
        {
            slot.Dispose();
        }
    }

    public void Run()
    {
        List<ItemDefinitionModel>[] sendNextRound = new List<ItemDefinitionModel>[Slots.Length];
        for (int i = 0; i < Slots.Length; i++)
        {
            Slots[i].Game.InitializeSpoilerData(PartialSpoilerData[i]);
            sendNextRound[i] = [];
        }

        Span<int> prevCheckedLocations = stackalloc int[Slots.Length];
        prevCheckedLocations.Clear();
        int steps = 0;
        while (true)
        {
            bool advanced = false;
            for (int i = 0; i < Slots.Length; i++)
            {
                World slot = Slots[i];
                if (slot.Game.IsCompleted)
                {
                    if (prevCheckedLocations[i] >= 0)
                    {
                        // this is the first time we hit the goal, so auto-release.
                        foreach ((LocationKey key, WorldItem itemToSend) in FullSpoilerData[i])
                        {
                            if (!slot.Game.CheckedLocations[key])
                            {
                                sendNextRound[itemToSend.Slot].Add(itemToSend.Item);
                            }
                        }

                        prevCheckedLocations[i] = -1;
                    }

                    continue;
                }

                slot.Game.Advance();
                advanced = true;
            }

            ++steps;
            if (!advanced)
            {
                break;
            }

            for (int i = 0; i < Slots.Length; i++)
            {
                if (prevCheckedLocations[i] < 0)
                {
                    continue;
                }

                CheckedLocations locs = Slots[i].Game.CheckedLocations;
                for (int j = prevCheckedLocations[i]; j < locs.Count; j++)
                {
                    WorldItem itemToSend = FullSpoilerData[i][locs.Order[j].Key];
                    sendNextRound[itemToSend.Slot].Add(GameDefinitions.Instance.ItemsByName[itemToSend.ItemName]);
                }

                prevCheckedLocations[i] = locs.Count;
            }

            for (int i = 0; i < Slots.Length; i++)
            {
                Slots[i].Game.ReceiveItems(CollectionsMarshal.AsSpan(sendNextRound[i]));
                CollectionsMarshal.SetCount(sendNextRound[i], 0);
            }

            if (steps > 100_000)
            {
                throw new InvalidOperationException("Pretty sure you're deadlocking at 100k.");
            }
        }
    }
}
