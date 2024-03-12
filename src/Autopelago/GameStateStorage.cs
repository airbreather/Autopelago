using ArchipelagoClientDotNet;

namespace Autopelago;

public abstract class GameStateStorage
{
    public virtual async ValueTask<Game.State?> LoadAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        return (await LoadProxyAsync(cancellationToken))?.ToState();
    }

    public virtual ValueTask SaveAsync(Game.State state, CancellationToken cancellationToken)
    {
        return SaveProxyAsync(state.ToProxy(), cancellationToken);
    }

    protected abstract ValueTask<Game.State.Proxy?> LoadProxyAsync(CancellationToken cancellationToken);

    protected abstract ValueTask SaveProxyAsync(Game.State.Proxy proxy, CancellationToken cancellationToken);
}
