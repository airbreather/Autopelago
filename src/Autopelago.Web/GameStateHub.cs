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

    public async ValueTask GetUpdate(string slotName)
    {
        await Helper.ConfigureAwaitFalse();
        IObservable<Game.State> gameStates = await _slotGameStates.GetGameStatesAsync(slotName, CancellationToken.None) ?? throw new InvalidOperationException("Bad slot name");
        Game.State state = await gameStates.FirstAsync().ToTask();

        Dictionary<string, int> inventory = GameDefinitions.Instance.ItemsByName.Keys.ToDictionary(k => k, _ => 0);
        foreach (ItemDefinitionModel item in state.ReceivedItems)
        {
            ++inventory[item.Name];
        }

        JsonObject obj = (JsonObject)JsonSerializer.SerializeToNode(state.ToProxy(), Game.State.Proxy.SerializerOptions)!;
        obj.Add("current_region", state.CurrentLocation.Key.RegionKey);
        obj.Add("rat_count", state.RatCount);
        obj.Add("completed_goal", state.IsCompleted);
        obj.Add("inventory", new JsonObject(inventory.Select(kvp => KeyValuePair.Create(kvp.Key, (JsonNode?)JsonValue.Create(kvp.Value)))));
        obj["checked_locations"] = new JsonArray([.. state.CheckedLocations.Where(l => l.Region is LandmarkRegionDefinitionModel).Select(l => l.Region.Key)]);

        await Clients.All.SendAsync("Updated", slotName, obj);
    }
}
