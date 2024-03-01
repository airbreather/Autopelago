using ArchipelagoClientDotNet;

using Microsoft.Extensions.Time.Testing;

public sealed class UnrandomizedTests
{
    [Fact]
    public async ValueTask FirstStepShouldStartAfterOneSecond()
    {
        using CancellationTokenSource cts = new();
        FakeTimeProvider timeProvider = new();
        UnrandomizedAutopelagoClient client = new();
        LocalGameStateStorage gameStateStorage = new();
        Game game = new(client, timeProvider);
        game.NextStepStarted += async (_, _, _) =>
        {
            await Helper.ConfigureAwaitFalse();
            await cts.CancelAsync();
        };

        ValueTask gameTask = game.RunUntilCanceledAsync(gameStateStorage, cts.Token);
        TimeSpan interval = TimeSpan.FromMilliseconds(1);
        for (TimeSpan totalAdvanced = TimeSpan.Zero; totalAdvanced < TimeSpan.FromSeconds(1); timeProvider.Advance(interval), totalAdvanced += interval)
        {
            Assert.False(cts.IsCancellationRequested);
        }

        Assert.True(cts.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await gameTask);
    }
}
