using Microsoft.Extensions.Time.Testing;

namespace Autopelago;

[TestFixture]
public sealed class GameTests
{
    private readonly UnrandomizedAutopelagoClient _client = new();

    private readonly FakeTimeProvider _timeProvider = new();

    [Test]
    public void FirstStepShouldStartAfterOneSecond()
    {
        bool hitNextState = false;
        Game game = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), _client, _timeProvider);
        game.StepStarted += (_, _, _) => { hitNextState = true; return ValueTask.CompletedTask; };
        using CancellationTokenSource cts = game.RunGameLoop(Game.State.Start());
        TimeSpan interval = TimeSpan.FromMilliseconds(1);
        for (TimeSpan totalAdvanced = TimeSpan.Zero; totalAdvanced < TimeSpan.FromSeconds(1); _timeProvider.Advance(interval), totalAdvanced += interval)
        {
            Assert.That(!hitNextState);
        }

        _timeProvider.Advance(interval);
        cts.Cancel();
        Assert.That(hitNextState);
    }
}
