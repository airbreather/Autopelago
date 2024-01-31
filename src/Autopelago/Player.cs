public sealed record Player
{
    public int ConsecutiveFailuresBeforeDiceModifierIncrement { get; init; } = 1;

    public int MovementSpeed { get; init; } = 1000;

    public Dictionary<Region, int> DiceModifier { get; } = Enum.GetValues<Region>().Select(region => KeyValuePair.Create(region, 0)).ToDictionary();
}
