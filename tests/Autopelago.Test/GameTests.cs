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
        bool hitNextState = false;
        using (Game.Run(Game.State.Start(), _client, _timeScheduler).Subscribe(_ => hitNextState = true))
        {
            TimeSpan interval = TimeSpan.FromMilliseconds(1);
            for (TimeSpan totalAdvanced = TimeSpan.Zero; totalAdvanced < TimeSpan.FromSeconds(1); _timeScheduler.AdvanceBy(interval.Ticks), totalAdvanced += interval)
            {
                Assert.That(!hitNextState);
            }

            _timeScheduler.AdvanceBy(interval.Ticks);
            Assert.That(hitNextState);
        }
    }
}
