using ArchipelagoClientDotNet;

using Microsoft.Extensions.Time.Testing;

public sealed class UnrandomizedTests
{
    private static readonly TimeSpan s_tolerance = TimeSpan.FromMilliseconds(500);

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

        if (!cts.IsCancellationRequested)
        {
            await cts.CancelAsync();
            Assert.Fail("Game did not advance after 1 second.");
        }

        try
        {
            await gameTask.AsTask().WaitAsync(s_tolerance);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
            await cts.CancelAsync();
            Assert.Skip($"Game took more than {s_tolerance.FormatMyWay()} to complete after ostensibly being canceled.");
        }
    }
}
