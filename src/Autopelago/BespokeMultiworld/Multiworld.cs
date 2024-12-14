using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace Autopelago.BespokeMultiworld;

public sealed class Multiworld : IDisposable
{
    public required ImmutableArray<World> Slots { get; init; }

    public required ImmutableArray<ImmutableArray<WorldItem>> FullSpoilerData { get; init; }

    public required ImmutableArray<FrozenDictionary<ArchipelagoItemFlags, BitArray384>> PartialSpoilerData { get; init; }

    public void Dispose()
    {
        foreach (World slot in Slots)
        {
            slot.Dispose();
        }
    }

    public void Run()
    {
        List<ItemKey>[] sendNextRound = new List<ItemKey>[Slots.Length];
        for (int i = 0; i < Slots.Length; i++)
        {
            Slots[i].Game.InitializeSpoilerData(PartialSpoilerData[i]);
            sendNextRound[i] = [];
        }

        Span<int> prevCheckedLocations = stackalloc int[Slots.Length];
        prevCheckedLocations.Clear();
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
                        ImmutableArray<WorldItem> spoilerData = FullSpoilerData[i];
                        for (int j = 0; j < spoilerData.Length; j++)
                        {
                            if (!slot.Game.LocationIsChecked[j])
                            {
                                sendNextRound[spoilerData[j].Slot].Add(spoilerData[j].Item);
                            }
                        }

                        prevCheckedLocations[i] = -1;
                    }

                    continue;
                }

                slot.Game.Advance();
                slot.Instrumentation.NextStep();
                advanced = true;
            }

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

                ReadOnlyCollection<LocationKey> locs = Slots[i].Game.CheckedLocations;
                for (int j = prevCheckedLocations[i]; j < locs.Count; j++)
                {
                    WorldItem itemToSend = FullSpoilerData[i][locs[j].N];
                    sendNextRound[itemToSend.Slot].Add(itemToSend.Item);
                }

                prevCheckedLocations[i] = locs.Count;
            }

            for (int i = 0; i < Slots.Length; i++)
            {
                Slots[i].Game.ReceiveItems(CollectionsMarshal.AsSpan(sendNextRound[i]));
                CollectionsMarshal.SetCount(sendNextRound[i], 0);
            }

            if (Slots[0].Instrumentation.StepNumber > 100_000)
            {
                throw new InvalidOperationException("Pretty sure you're deadlocking at 100k.");
            }
        }
    }
}
