public sealed record GameDifficultySettings
{
    public int RegionChangeSteps { get; init; } = 3500;

    public Dictionary<Region, int> DifficultyClass { get; } = new()
    {
        [Region.BeforeBasketball] = 10,
        [Region.Basketball] = 11,
        [Region.BeforeMinotaur] = 12,
        [Region.BeforePrawnStars] = 12,
        [Region.Minotaur] = 12,
        [Region.PrawnStars] = 12,
        [Region.BeforeRestaurant] = 14,
        [Region.BeforePirateBakeSale] = 14,
        [Region.Restaurant] = 14,
        [Region.PirateBakeSale] = 14,
        [Region.AfterRestaurant] = 16,
        [Region.AfterPirateBakeSale] = 16,
        [Region.BowlingBallDoor] = 18,
        [Region.Goldfish] = 20,
    };
}
