using Microsoft.Extensions.Time.Testing;

namespace Autopelago;

public sealed class GameTests
{
    private readonly UnrandomizedAutopelagoClient _client = new();

    private readonly FakeTimeProvider _timeProvider = new();

    private readonly LocalGameStateStorage _gameStateStorage = new();

    private readonly Game _game;

    public GameTests()
    {
        _game = new(_client, _timeProvider);
    }

    [Fact]
    public void FirstStepShouldStartAfterOneSecond()
    {
        using CancellationTokenSource cts = new();
        bool advanced = false;
        _game.NextStepStarted += (_, _, _) =>
        {
            cts.Cancel();
            advanced = true;
            return ValueTask.CompletedTask;
        };

        ValueTask gameTask = _game.RunUntilCanceledAsync(_gameStateStorage, cts.Token);
        TimeSpan interval = TimeSpan.FromMilliseconds(1);
        for (TimeSpan totalAdvanced = TimeSpan.Zero; totalAdvanced < TimeSpan.FromSeconds(1); _timeProvider.Advance(interval), totalAdvanced += interval)
        {
            Assert.False(cts.IsCancellationRequested);
        }

        if (!advanced)
        {
            cts.Cancel();
            Assert.Fail("Game did not advance after 1 second.");
        }
    }
}
