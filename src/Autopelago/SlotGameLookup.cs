using System.Collections.Frozen;

namespace Autopelago;

public sealed class SlotGameLookup
{
    private readonly FrozenDictionary<string, TaskCompletionSource<Game?>> _slots;

    public SlotGameLookup(AutopelagoSettingsModel settings)
    {
        _slots = settings.Slots.ToFrozenDictionary(s => s.Name, _ => new TaskCompletionSource<Game?>(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    public ValueTask<Game?> GetGameAsync(string slotName, CancellationToken cancellationToken)
    {
        return _slots.TryGetValue(slotName, out TaskCompletionSource<Game?>? slot)
            ? new(slot.Task.WaitAsync(cancellationToken))
            : ValueTask.FromResult(default(Game));
    }

    public bool InitGame(string slotName, Game game)
    {
        return _slots.TryGetValue(slotName, out TaskCompletionSource<Game?>? slot) && slot.TrySetResult(game);
    }
}
