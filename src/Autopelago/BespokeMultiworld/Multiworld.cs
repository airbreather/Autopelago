using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Autopelago.BespokeMultiworld;

public sealed class Multiworld : IDisposable
{
    public required ImmutableArray<World> Slots { get; init; }

    public required ImmutableArray<FrozenDictionary<LocationKey, WorldItem>> FullSpoilerData { get; init; }

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
            Slots[i].Game.InitializeSpoilerData(FullSpoilerData[i].ToFrozenDictionary(kvp => kvp.Key, kvp => GameDefinitions.Instance.ItemsByName[kvp.Value.ItemName].ArchipelagoFlags));
            sendNextRound[i] = [];
        }

        Span<int> prevCheckedLocations = stackalloc int[Slots.Length];
        prevCheckedLocations.Clear();
        while (true)
        {
            bool advanced = false;
            bool allBK = true;
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
                allBK &= slot.Game.TargetLocationReason == TargetLocationReason.NowhereUsefulToMove;
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

                CheckedLocations locs = Slots[i].Game.CheckedLocations;
                for (int j = prevCheckedLocations[i]; j < locs.Count; j++)
                {
                    WorldItem itemToSend = FullSpoilerData[i][locs.Order[j].Key];
                    sendNextRound[itemToSend.Slot].Add(GameDefinitions.Instance.ItemsByName[itemToSend.ItemName]);
                }

                prevCheckedLocations[i] = locs.Count;
            }

            bool sentAny = false;
            for (int i = 0; i < Slots.Length; i++)
            {
                Slots[i].Game.ReceiveItems(CollectionsMarshal.AsSpan(sendNextRound[i]));
                sentAny |= sendNextRound[i].Count > 0;
                CollectionsMarshal.SetCount(sendNextRound[i], 0);
            }

            if (allBK && !sentAny)
            {
                throw new InvalidOperationException("All BK!");
            }
        }
    }
}
