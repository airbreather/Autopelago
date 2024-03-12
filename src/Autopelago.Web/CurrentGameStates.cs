using System.Collections.Immutable;

using ArchipelagoClientDotNet;

namespace Autopelago.Web;

public sealed record GameStateAdvancedEventArgs
{
    public required string SlotName { get; init; }

    public required Game.State? StateBeforeAdvance { get; init; }

    public required Game.State StateAfterAdvance { get; init; }
}

public sealed class CurrentGameStates
{
    private readonly AsyncEvent<GameStateAdvancedEventArgs> _gameStateAdvancedEvent = new();

    private ImmutableDictionary<string, Game.State> _states = ImmutableDictionary<string, Game.State>.Empty;

    public event AsyncEventHandler<GameStateAdvancedEventArgs> GameStateAdvancedEvent
    {
        add => _gameStateAdvancedEvent.Add(value);
        remove => _gameStateAdvancedEvent.Remove(value);
    }

    public Game.State? Get(string slotName)
    {
        _states.TryGetValue(slotName, out Game.State? state);
        return state;
    }

    public async ValueTask SetAsync(string slotName, Game.State newState, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        _states.TryGetValue(slotName, out Game.State? prevState);
        GameStateAdvancedEventArgs args = new()
        {
            SlotName = slotName,
            StateBeforeAdvance = prevState,
            StateAfterAdvance = newState,
        };

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImmutableDictionary<string, Game.State> prevStates = _states;
            if (Interlocked.CompareExchange(ref _states, prevStates.SetItem(slotName, newState), prevStates) == prevStates)
            {
                await _gameStateAdvancedEvent.InvokeAsync(this, args, cancellationToken);
                break;
            }
        }
    }
}
