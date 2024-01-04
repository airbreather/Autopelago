public sealed record GameDifficultySettings
{
    public int RegionChangeSteps { get; init; } = 4;

    public Dictionary<Region, int> DifficultyClass { get; } = new()
    {
        [Region.Before8Rats] = 10,
        [Region.After8RatsBeforeA] = 12,
        [Region.After8RatsBeforeB] = 12,
        [Region.A] = 12,
        [Region.B] = 12,
        [Region.AfterABeforeC] = 14,
        [Region.AfterBBeforeD] = 14,
        [Region.C] = 14,
        [Region.D] = 14,
        [Region.AfterCBefore20Rats] = 16,
        [Region.AfterDBefore20Rats] = 16,
        [Region.After20RatsBeforeE] = 18,
        [Region.After20RatsBeforeF] = 18,
        [Region.E] = 18,
        [Region.F] = 18,
        [Region.TryingForGoal] = 20,
    };
}
