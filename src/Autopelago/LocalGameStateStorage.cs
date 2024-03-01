public sealed class LocalGameStateStorage : GameStateStorage
{
    public Game.State? State { get; set; }

    public override ValueTask<Game.State?> LoadAsync(CancellationToken cancellationToken)
    {
        return new(State);
    }

    public override ValueTask SaveAsync(Game.State state, CancellationToken cancellationToken)
    {
        State = state;
        return ValueTask.CompletedTask;
    }

    protected override ValueTask<Game.State.Proxy?> LoadProxyAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException("We bypass this in favor of capturing the Game.State itself.");
    }

    protected override ValueTask SaveProxyAsync(Game.State.Proxy proxy, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("We bypass this in favor of capturing the Game.State itself.");
    }
}
