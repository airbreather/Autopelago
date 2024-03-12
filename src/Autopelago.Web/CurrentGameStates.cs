using System.Collections.Immutable;

namespace Autopelago.Web;

public sealed class CurrentGameStates
{
    private ImmutableDictionary<string, Game.State> _states = ImmutableDictionary<string, Game.State>.Empty;

    public Game.State? Get(string slotName)
    {
        _states.TryGetValue(slotName, out Game.State? state);
        return state;
    }

    public void Set(string slotName, Game.State newState)
    {
        while (true)
        {
            ImmutableDictionary<string, Game.State> prevStates = _states;
            if (Interlocked.CompareExchange(ref _states, prevStates.SetItem(slotName, newState), prevStates) == prevStates)
            {
                break;
            }
        }
    }
}
