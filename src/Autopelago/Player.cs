public sealed class Player
{
    public Game.State Advance(Game.State state)
    {
        return state with { Epoch = state.Epoch + 1 };
    }
}
