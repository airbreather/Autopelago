using System.Collections.Frozen;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;

using ArchipelagoClientDotNet;

using Microsoft.AspNetCore.SignalR;

namespace Autopelago.Web;

public sealed class GameStateHub : Hub
{
    private readonly SlotGameStates _slotGameStates;

    public GameStateHub(SlotGameStates slotGameStates)
    {
        _slotGameStates = slotGameStates;
    }

    public async ValueTask GetSlots()
    {
        await Helper.ConfigureAwaitFalse();

        FrozenDictionary<string, IObservable<Game.State>> allStates = await _slotGameStates.GameStatesMappingBox.Task;

        await Clients.All.SendAsync("GotSlots", allStates.Keys.Order());
    }

    public async ValueTask GetUpdate(string slotName, ulong epoch)
    {
        await Helper.ConfigureAwaitFalse();
        IObservable<Game.State> gameStates = await _slotGameStates.GetGameStatesAsync(slotName, CancellationToken.None) ?? throw new InvalidOperationException("Bad slot name");
        Game.State state = await gameStates.FirstAsync(s => s.Epoch > epoch).ToTask();

        Dictionary<string, int> inventory = GameDefinitions.Instance.ItemsByName.Keys.ToDictionary(k => k, _ => 0);
        foreach (ItemDefinitionModel item in state.ReceivedItems)
        {
            ++inventory[item.Name];
        }

        HashSet<string> openRegions = [];
        Queue<RegionDefinitionModel> regions = [];
        regions.Enqueue(GameDefinitions.Instance.StartRegion);
        while (regions.TryDequeue(out RegionDefinitionModel? region))
        {
            openRegions.Add(region.Key);
            foreach (RegionExitDefinitionModel exit in region.Exits)
            {
                if (exit.Requirement.StaticSatisfied(state))
                {
                    regions.Enqueue(GameDefinitions.Instance.AllRegions[exit.RegionKey]);
                }
            }
        }

        JsonObject obj = (JsonObject)JsonSerializer.SerializeToNode(state.ToProxy(), Game.State.Proxy.SerializerOptions)!;
        obj.Add("current_region", state.CurrentLocation.Key.RegionKey);
        obj.Add("rat_count", state.RatCount);
        obj.Add("completed_goal", state.IsCompleted);
        obj.Add("inventory", new JsonObject(inventory.Select(kvp => KeyValuePair.Create(kvp.Key, (JsonNode?)JsonValue.Create(kvp.Value)))));
        obj["checked_locations"] = new JsonArray([.. state.CheckedLocations.Where(l => l.Region is LandmarkRegionDefinitionModel).Select(l => l.Region.Key)]);
        obj["open_regions"] = new JsonArray([.. openRegions]);

        await Clients.All.SendAsync("Updated", slotName, obj);
    }
}
