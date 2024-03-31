using Microsoft.Extensions.Time.Testing;

namespace Autopelago;

[TestFixture]
public sealed class GameTests
{
    private readonly FakeAutopelagoClient _client = new();

    private readonly FakeTimeProvider _timeProvider = new();

    [Test]
    public void FirstStepShouldStartAfterOneSecond()
    {
        TimeSpan fullInterval = TimeSpan.FromSeconds(1);
        Game game = new(fullInterval, fullInterval, _client, _timeProvider);
        using CancellationTokenSource cts = new();

        _timeProvider.Advance(fullInterval);
        ValueTask<Game.State> advanceFirstTask = game.AdvanceFirstAsync(Game.State.Start(), cts.Token);
        Assert.That(advanceFirstTask.IsCompletedSuccessfully);

        ValueTask<Game.State> advanceAgainTask = game.AdvanceOnceAsync(cts.Token);
        _timeProvider.Advance(fullInterval - TimeSpan.FromTicks(1));
        cts.Cancel();
        Assert.That(!advanceAgainTask.IsCompletedSuccessfully);
    }

    [Test]
    public void AurasShouldComeWithItems()
    {
        TimeSpan fullInterval = TimeSpan.FromSeconds(1);
        Game game = new(fullInterval, fullInterval, _client, _timeProvider);
        _timeProvider.Advance(fullInterval);

        Task advanceFirstTask = game.AdvanceFirstAsync(Game.State.Start(), CancellationToken.None).AsTask();
        Assert.That(advanceFirstTask.IsCompletedSuccessfully);

        Task receivedItemsEventTask = _client.TriggerReceivedItemsEvent(new()
        {
            Index = 0,
            Items = [ GameDefinitions.Instance.ItemsByName["5th Ace"] ],
        }).AsTask();

        Assert.Multiple(() =>
        {
            Assert.That(receivedItemsEventTask.IsCompletedSuccessfully);
            Assert.That(game.CurrentState?.ActiveAuraEffects, Is.EqualTo(new[] { LuckyEffect.Instance }));
        });
    }
}
