using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

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
        await Clients.All.SendAsync("Updated", slotName, state.ToProxy());
    }
}
