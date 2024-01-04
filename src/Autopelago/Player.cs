public sealed record Player
{
    public int ConsecutiveFailuresBeforeDiceModifierIncrement { get; init; } = 1;

    public int MovementSpeed { get; init; } = 1;

    public Dictionary<Region, int> DiceModifier { get; } = new()
    {
        [Region.Before8Rats] = 0,
        [Region.After8RatsBeforeA] = 0,
        [Region.After8RatsBeforeB] = 0,
        [Region.A] = 0,
        [Region.B] = 0,
        [Region.AfterABeforeC] = 0,
        [Region.AfterBBeforeD] = 0,
        [Region.C] = 0,
        [Region.D] = 0,
        [Region.AfterCBefore20Rats] = 0,
        [Region.AfterDBefore20Rats] = 0,
        [Region.After20RatsBeforeE] = 0,
        [Region.After20RatsBeforeF] = 0,
        [Region.E] = 0,
        [Region.F] = 0,
        [Region.TryingForGoal] = 0,
    };
}
