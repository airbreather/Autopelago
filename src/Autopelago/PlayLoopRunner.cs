using System.Collections.Immutable;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;

namespace Autopelago;

public sealed class PlayLoopRunner : IDisposable
{
    private static readonly ImmutableArray<string> s_blockedMessages =
    [
        "I don't have anything to do right now. Go team!",
        "Hey, I'm completely stuck. But I still believe in you!",
        "I've run out of things to do. How are you?",
        "I'm out of things for now, gonna get a coffee. Anyone want something?",
    ];

    private static readonly ImmutableArray<string> s_unblockedMessages =
    [
        "Yippee, that's just what I needed!",
        "I'm back! I knew you could do it!",
        "Sweet, I'm unblocked! Thanks!",
        "Squeak-squeak, it's rattin' time!",
    ];

    private readonly CompositeDisposable _disposables = [];

    private readonly BehaviorSubject<Game> _gameUpdates;

    private readonly ImmutableArray<long> _locationIds;

    private readonly ArchipelagoPacketProvider _packets;

    private readonly Settings _settings;

    private readonly TimeProvider _timeProvider;

    public PlayLoopRunner(Game game, MultiworldInfo context, ArchipelagoPacketProvider packets, Settings settings, TimeProvider timeProvider)
    {
        _disposables.Add(_gameUpdates = new(game));
        GameUpdates = _gameUpdates.AsObservable();
        _locationIds = context.LocationIds;
        _packets = packets;
        _settings = settings;
        _timeProvider = timeProvider;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public IObservable<Game> GameUpdates { get; }

    public async Task RunPlayLoopAsync(BehaviorSubject<bool> pausedSubject, CancellationToken cancellationToken)
    {
        PauseUnpause pauseUnpause = new()
        {
            Paused = pausedSubject.Value,
            CancellationTokenSource = new(),
        };
        using IDisposable _ = pausedSubject.Subscribe(paused =>
        {
            PauseUnpause oldPauseUnpause = pauseUnpause;
            pauseUnpause = new()
            {
                Paused = paused,
                CancellationTokenSource = new(),
            };
            oldPauseUnpause.CancellationTokenSource.Cancel();
        });

        Game game = _gameUpdates.Value;
        bool wasGoMode = false;
        bool wasCompleted = false;
        long prevStartTimestamp = _timeProvider.GetTimestamp();
        long? prevBlockedReportTimestampOrNull = null;
        TimeSpan nextFullInterval = NextInterval();
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan remaining = nextFullInterval - _timeProvider.GetElapsedTime(prevStartTimestamp);
            if (remaining > TimeSpan.Zero)
            {
                long dueTime = _timeProvider.GetTimestamp() + ((long)(nextFullInterval.TotalSeconds * _timeProvider.TimestampFrequency));
                while (_timeProvider.GetTimestamp() < dueTime)
                {
                    PauseUnpause localPauseUnpause = pauseUnpause;
                    long waitStart = _timeProvider.GetTimestamp();
                    if (await localPauseUnpause.WaitUntilUnpausedAsync(cancellationToken))
                    {
                        dueTime += _timeProvider.GetTimestamp() - waitStart;
                        continue;
                    }

                    using CancellationTokenSource pauseOrCancel = CancellationTokenSource.CreateLinkedTokenSource(localPauseUnpause.CancellationTokenSource.Token, cancellationToken);
                    try
                    {
                        await Task.Delay(remaining, _timeProvider, pauseOrCancel.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // user clicked Pause
                    }
                }
            }

            await pauseUnpause.WaitUntilUnpausedAsync(cancellationToken);
            prevStartTimestamp = _timeProvider.GetTimestamp();
            int prevCheckedLocationsCount = game.CheckedLocations.Count;
            nextFullInterval = NextInterval();
            game.Advance();
            if (!wasGoMode && game.TargetLocationReason == TargetLocationReason.GoMode)
            {
                SayPacketModel say = new()
                {
                    Text = "That's it! I have everything I need! The moon is in sight!",
                };
                await _packets.SendPacketsAsync([say]);
                wasGoMode = true;
            }

            _gameUpdates.OnNext(game);

            List<long> locationIds = [];
            foreach (LocationKey location in game.CheckedLocations.Skip(prevCheckedLocationsCount))
            {
                locationIds.Add(_locationIds[location.N]);
            }

            if (locationIds.Count > 0)
            {
                LocationChecksPacketModel locationChecks = new() { Locations = locationIds.ToArray() };
                await _packets.SendPacketsAsync([locationChecks]);
            }

            if (game.TargetLocationReason == TargetLocationReason.NowhereUsefulToMove)
            {
                if (!game.IsCompleted)
                {
                    if (prevBlockedReportTimestampOrNull is long prevBlockedReportTimestamp)
                    {
                        if (Stopwatch.GetElapsedTime(prevBlockedReportTimestamp).TotalMinutes >= 15)
                        {
                            prevBlockedReportTimestampOrNull = null;
                        }
                    }

                    if (prevBlockedReportTimestampOrNull is null)
                    {
                        await _packets.SendPacketsAsync([new SayPacketModel
                        {
                            Text = s_blockedMessages[Random.Shared.Next(s_blockedMessages.Length)],
                        }]);
                        prevBlockedReportTimestampOrNull = Stopwatch.GetTimestamp();
                    }
                }
            }
            else
            {
                if (prevBlockedReportTimestampOrNull is not null)
                {
                    await _packets.SendPacketsAsync([new SayPacketModel
                    {
                        Text = s_unblockedMessages[Random.Shared.Next(s_unblockedMessages.Length)],
                    }]);
                    prevBlockedReportTimestampOrNull = null;
                }
            }

            if (game.IsCompleted && !wasCompleted)
            {
                await _packets.SendPacketsAsync([new SayPacketModel
                {
                    Text = "Yeah, I did it! er... WE did it!",
                }]);

                StatusUpdatePacketModel statusUpdate = new() { Status = ArchipelagoClientStatus.Goal };
                await _packets.SendPacketsAsync([statusUpdate]);
                _gameUpdates.OnNext(game);
                wasCompleted = true;
            }
        }
    }

    private TimeSpan NextInterval()
    {
        double rangeSeconds = (double)(_settings.MaxStepSeconds - _settings.MinStepSeconds);
        double baseInterval = (double)_settings.MinStepSeconds + (rangeSeconds * Random.Shared.NextDouble());
        return TimeSpan.FromSeconds(baseInterval);
    }
}

[StructLayout(LayoutKind.Auto)]
file readonly record struct PauseUnpause
{
    public required bool Paused { get; init; }

    public required CancellationTokenSource CancellationTokenSource { get; init; }

    public async ValueTask<bool> WaitUntilUnpausedAsync(CancellationToken cancellationToken = default)
    {
        if (!Paused)
        {
            return false;
        }

        TaskCompletionSource cancelTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using CancellationTokenRegistration reg = CancellationTokenSource.Token.Register(() => cancelTcs.TrySetResult());
        await cancelTcs.Task.WaitAsync(cancellationToken);
        return true;
    }
}
