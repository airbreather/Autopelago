using System.Reactive.Linq;

using Microsoft.Reactive.Testing;

namespace Autopelago;

[TestFixture]
public sealed class GameTests
{
    private readonly UnrandomizedAutopelagoClient _client = new();

    private readonly TestScheduler _timeScheduler = new();

    [Test]
    public void FirstStepShouldStartAfterOneSecond()
    {
        using CancellationTokenSource cts = new();
        bool transitioned = false;

        IObservable<Game.State> obs = Game.Run(Game.State.Start(), _client, _timeScheduler);
        using (obs.Subscribe(_ => transitioned = true))
        {
            TimeSpan interval = TimeSpan.FromMilliseconds(1);
            for (TimeSpan totalAdvanced = TimeSpan.Zero; totalAdvanced < TimeSpan.FromSeconds(1); _timeScheduler.AdvanceBy(interval.Ticks), totalAdvanced += interval)
            {
                Assert.That(!transitioned);
            }

            _timeScheduler.AdvanceBy(interval.Ticks);
            Assert.That(transitioned);
        }
    }
}
