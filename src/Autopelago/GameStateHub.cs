using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

using Microsoft.AspNetCore.SignalR;

namespace Autopelago;

public sealed class GameStateHub : Hub
{
    private static readonly BoundedChannelOptions s_boundedChannelOptions = new(1)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.DropOldest,
    };

    private readonly AutopelagoSettingsModel _settings;

    private readonly SlotGameLookup _slotGameLookup;

    public GameStateHub(AutopelagoSettingsModel settings, SlotGameLookup slotGameLookup)
    {
        _settings = settings;
        _slotGameLookup = slotGameLookup;
    }

    public async ValueTask GetSlots()
    {
        await Helper.ConfigureAwaitFalse();
        await Clients.All.SendAsync("GotSlots", _settings.Slots.Select(s => s.Name));
    }

    public ChannelReader<JsonObject> GetSlotUpdates(string slotName, CancellationToken cancellationToken)
    {
        Channel<JsonObject> channel = Channel.CreateBounded<JsonObject>(s_boundedChannelOptions);
        ValueTask<Game?> gameTask = _slotGameLookup.GetGameAsync(slotName, cancellationToken);
        if (gameTask.IsCompletedSuccessfully && gameTask.GetAwaiter().GetResult() is null)
        {
            channel.Writer.Complete(new ArgumentException("Slot was not from GotSlots.", nameof(slotName)));
            return channel.Reader;
        }

        _ = Task.Run(async () => await WriteSlotUpdatesAsync(gameTask, channel.Writer, cancellationToken), cancellationToken);
        return channel.Reader;
    }

    private static async Task WriteSlotUpdatesAsync(ValueTask<Game?> gameTask, ChannelWriter<JsonObject> writer, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        Exception? ex = null;
        try
        {
            Game game = (await gameTask) ??
                throw new InvalidOperationException("Game task should only be able to yield null in the synchronous part. This is a programming error in the backend.");

            Game.State? next = game.CurrentState;
            while (true)
            {
                ValueTask<Game.State> nextTask = NextGameStateAsync(game, next?.Epoch, cancellationToken);
                if (next is not null)
                {
                    await writer.WriteAsync(ToJsonObject(next), cancellationToken);
                    if (next.IsCompleted)
                    {
                        break;
                    }
                }

                next = await nextTask;
            }
        }
        catch (Exception innerEx)
        {
            ex = innerEx;
        }
        finally
        {
            writer.Complete(ex);
        }
    }

    private static ValueTask<Game.State> NextGameStateAsync(Game game, ulong? prevEpochOrNull, CancellationToken cancellationToken)
    {
        return Helper.NextAsync<GameStateEventArgs, Game.State>(
            subscribe: e => game.StateChanged += e,
            unsubscribe: e => game.StateChanged -= e,
            predicate: args => prevEpochOrNull is not ulong prevEpoch || args.CurrentState.Epoch > prevEpoch,
            selector: args => args.CurrentState,
            cancellationToken: cancellationToken);
    }

    private static JsonObject ToJsonObject(Game.State state)
    {
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

        return obj;
    }
}
