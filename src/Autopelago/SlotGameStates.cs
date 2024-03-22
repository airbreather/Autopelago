using System.Collections.Frozen;

namespace Autopelago;

public sealed class SlotGameStates
{
    public TaskCompletionSource<FrozenDictionary<string, IObservable<Game.State>>> GameStatesMappingBox { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask<IObservable<Game.State>?> GetGameStatesAsync(string slotName, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        FrozenDictionary<string, IObservable<Game.State>> gameStatesMapping = await GameStatesMappingBox.Task.WaitAsync(cancellationToken);
        gameStatesMapping.TryGetValue(slotName, out IObservable<Game.State>? gameStates);
        return gameStates;
    }
}
