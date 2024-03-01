using ArchipelagoClientDotNet;

using Microsoft.Extensions.Time.Testing;

public sealed class UnrandomizedTests
{
    private static readonly TimeSpan s_tolerance = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async ValueTask TestDelay()
    {
        using CancellationTokenSource cts = new();
        FakeTimeProvider timeProvider = new();
        UnrandomizedAutopelagoClient client = new();
        LocalGameStateStorage gameStateStorage = new();
        using SemaphoreSlim firstStepStarted = new(0, 1);
        Game game = new(client, timeProvider);
        game.NextStepStarted += (_, _, _) =>
        {
            firstStepStarted.Release();
            return ValueTask.CompletedTask;
        };

        using SemaphoreSlim gameStarted = new(0, 1);
        Task gameTask = BackgroundTaskRunner.Run(async () =>
        {
            await Helper.ConfigureAwaitFalse();
            ValueTask innerTask = game.RunUntilCanceledAsync(gameStateStorage, cts.Token);
            gameStarted.Release();
            await innerTask;
        }, cts.Token);

        await gameStarted.WaitAsync();
        TimeSpan interval = TimeSpan.FromMilliseconds(1);
        for (TimeSpan totalAdvanced = TimeSpan.Zero; totalAdvanced < TimeSpan.FromSeconds(1); timeProvider.Advance(interval), totalAdvanced += interval)
        {
            Assert.False(firstStepStarted.Wait(0));
        }

        Assert.True(await firstStepStarted.WaitAsync(s_tolerance));
    }
}
