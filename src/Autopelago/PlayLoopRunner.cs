using System.Collections.Immutable;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;

namespace Autopelago;

public sealed class PlayLoopRunner : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    private readonly BehaviorSubject<Game> _gameUpdates;

    private readonly WeightedRandomItems<WeightedString> _enterBKMessages;

    private readonly WeightedRandomItems<WeightedString> _remindBKMessages;

    private readonly WeightedRandomItems<WeightedString> _exitBKMessages;

    private readonly WeightedRandomItems<WeightedString> _enteredGoModeMessages;

    private readonly WeightedRandomItems<WeightedString> _completedGoalMessages;

    private readonly ImmutableArray<long> _locationIds;

    private readonly string _serverSavedStateKey;

    private readonly ArchipelagoPacketProvider _packets;

    private readonly Settings _settings;

    private readonly TimeProvider _timeProvider;

    public PlayLoopRunner(Game game, MultiworldInfo context, ArchipelagoPacketProvider packets, Settings settings, TimeProvider timeProvider)
    {
        _disposables.Add(_gameUpdates = new(game));
        GameUpdates = _gameUpdates.AsObservable();
        _locationIds = context.LocationIds;
        _serverSavedStateKey = context.ServerSavedStateKey;
        _enterBKMessages = context.EnterBKMessages;
        _remindBKMessages = context.RemindBKMessages;
        _exitBKMessages = context.ExitBKMessages;
        _enteredGoModeMessages = context.EnteredGoModeMessages;
        _completedGoalMessages = context.CompletedGoalMessages;
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
        bool hadCompletedGoal = false;
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
                if (_settings.RatChat && _settings.RatChatForOneTimeEvents)
                {
                    await _packets.SendPacketsAsync([new SayPacketModel
                    {
                        Text = _enteredGoModeMessages.Roll(),
                    }]);
                }

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
                    bool remind = false;
                    if (prevBlockedReportTimestampOrNull is long prevBlockedReportTimestamp && _settings.RatChatForFirstBlocked && _settings.RatChatForStillBlocked)
                    {
                        if (Stopwatch.GetElapsedTime(prevBlockedReportTimestamp).TotalMinutes >= 15)
                        {
                            remind = true;
                        }
                    }

                    if (prevBlockedReportTimestampOrNull is null || remind)
                    {
                        if (_settings.RatChat && _settings.RatChatForFirstBlocked)
                        {
                            await _packets.SendPacketsAsync([new SayPacketModel
                            {
                                Text = (remind ? _remindBKMessages : _enterBKMessages).Roll(),
                            }]);
                        }

                        prevBlockedReportTimestampOrNull = Stopwatch.GetTimestamp();
                    }
                }
            }
            else
            {
                if (prevBlockedReportTimestampOrNull is not null)
                {
                    if (_settings.RatChat && _settings.RatChatForUnblocked)
                    {
                        await _packets.SendPacketsAsync([new SayPacketModel
                        {
                            Text = _exitBKMessages.Roll(),
                        }]);
                    }

                    prevBlockedReportTimestampOrNull = null;
                }
            }

            await _packets.SendPacketsAsync([_packets.CreateUpdateStatePacket(game, _serverSavedStateKey)]);

            if (game.HasCompletedGoal && !hadCompletedGoal)
            {
                StatusUpdatePacketModel statusUpdate = new() { Status = ArchipelagoClientStatus.Goal };
                await _packets.SendPacketsAsync([statusUpdate]);
                _gameUpdates.OnNext(game);
                hadCompletedGoal = true;
            }

            if (game.IsCompleted && !wasCompleted)
            {
                if (_settings.RatChat && _settings.RatChatForOneTimeEvents)
                {
                    await _packets.SendPacketsAsync([new SayPacketModel
                    {
                        Text = _completedGoalMessages.Roll(),
                    }]);
                }

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
