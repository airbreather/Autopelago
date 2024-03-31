using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autopelago;

public record GameStateEventArgs
{
    public required Game.State CurrentState { get; init; }
}

public sealed record StepStartedEventArgs : GameStateEventArgs
{
}

public sealed record StepFinishedEventArgs : GameStateEventArgs
{
    public required Game.State StateBeforeAdvance { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Game.State.Proxy))]
internal sealed partial class GameStateProxySerializerContext : JsonSerializerContext
{
}

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Game has a lifetime that's basically the same as the application's own, so this would be overkill at this time.")]
public sealed class Game
{
    public sealed record State
    {
        private State()
        {
        }

        private State(State copyFrom)
        {
            Epoch = copyFrom.Epoch + 1;
            TotalNontrivialStepCount = copyFrom.TotalNontrivialStepCount;
            CurrentLocation = copyFrom.CurrentLocation;
            TargetLocation = copyFrom.TargetLocation;
            ReceivedItems = copyFrom.ReceivedItems;
            CheckedLocations = copyFrom.CheckedLocations;
            ActiveAuraEffects = copyFrom.ActiveAuraEffects;
            PrngState = copyFrom.PrngState;
            LocationCheckAttemptsThisStep = copyFrom.LocationCheckAttemptsThisStep;
        }

        public ulong Epoch { get; private init; }

        public required ulong TotalNontrivialStepCount { get; init; }

        public required LocationDefinitionModel CurrentLocation { get; init; }

        public required LocationDefinitionModel TargetLocation { get; init; }

        public required ImmutableList<ItemDefinitionModel> ReceivedItems { get; init; }

        public required ImmutableList<LocationDefinitionModel> CheckedLocations { get; init; }

        public required ImmutableList<AuraEffect> ActiveAuraEffects { get; init; }

        public required int LocationCheckAttemptsThisStep { get; init; }

        public required Prng.State PrngState { get; init; }

        public double IntervalDurationMultiplier => 1;

        public bool IsCompleted => CurrentLocation == GameDefinitions.Instance.GoalLocation;

        public int DiceModifier => (RatCount / 3) - (LocationCheckAttemptsThisStep * 5) - (ActiveAuraEffects.Any(a => a is UnluckyEffect) ? 5 : 0);

        public int RatCount => ReceivedItems.Sum(i => i.RatCount).GetValueOrDefault();

        public static State Start(Random? random = null)
        {
            return Start(unchecked((ulong)(random ?? Random.Shared).NextInt64()));
        }

        public static State Start(ulong seed)
        {
            return Start(Prng.State.Start(seed));
        }

        public static State Start(Prng.State prngState)
        {
            return new()
            {
                TotalNontrivialStepCount = 0,
                CurrentLocation = GameDefinitions.Instance.StartLocation,
                TargetLocation = GameDefinitions.Instance.StartLocation,
                ReceivedItems = [],
                CheckedLocations = [],
                ActiveAuraEffects = [],
                LocationCheckAttemptsThisStep = 0,
                PrngState = prngState,
            };
        }

        public static int NextD20(ref State state)
        {
            Prng.State s = state.PrngState;
            int result = Prng.NextD20(ref s);
            state = state with { PrngState = s };
            return result;
        }

        public Proxy ToProxy()
        {
            return new()
            {
                Epoch = Epoch,
                TotalNontrivialStepCount = TotalNontrivialStepCount,
                CurrentLocation = CurrentLocation.Name,
                TargetLocation = TargetLocation.Name,
                ReceivedItems = [.. ReceivedItems.Select(i => i.Name)],
                CheckedLocations = [.. CheckedLocations.Select(l => l.Name)],
                ActiveAuraEffects = ActiveAuraEffects,
                LocationCheckAttemptsThisStep = LocationCheckAttemptsThisStep,
                PrngState = PrngState,
            };
        }

        public bool Equals(State? other)
        {
            return
                other is not null &&
                Epoch == other.Epoch &&
                PrngState == other.PrngState &&
                TotalNontrivialStepCount == other.TotalNontrivialStepCount &&
                CurrentLocation == other.CurrentLocation &&
                TargetLocation == other.TargetLocation &&
                LocationCheckAttemptsThisStep == other.LocationCheckAttemptsThisStep &&
                ReceivedItems.SequenceEqual(other.ReceivedItems) &&
                CheckedLocations.SequenceEqual(other.CheckedLocations) &&
                ActiveAuraEffects.SequenceEqual(other.ActiveAuraEffects);
        }

        public override int GetHashCode() => Epoch.GetHashCode();

        public sealed record Proxy
        {
            public static readonly JsonSerializerOptions SerializerOptions = new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = GameStateProxySerializerContext.Default,
            };

            public ulong Epoch { get; init; }

            public required ulong TotalNontrivialStepCount { get; init; }

            public required string CurrentLocation { get; init; }

            public required string TargetLocation { get; init; }

            public required ImmutableArray<string> ReceivedItems { get; init; }

            public required ImmutableArray<string> CheckedLocations { get; init; }

            public required ImmutableList<AuraEffect> ActiveAuraEffects { get; init; }

            public required int LocationCheckAttemptsThisStep { get; init; }

            public Prng.State PrngState { get; init; }

            public State ToState()
            {
                return new()
                {
                    Epoch = Epoch,
                    TotalNontrivialStepCount = TotalNontrivialStepCount,
                    CurrentLocation = GameDefinitions.Instance.LocationsByName[CurrentLocation],
                    TargetLocation = GameDefinitions.Instance.LocationsByName[TargetLocation],
                    ReceivedItems = [.. ReceivedItems.Select(name => GameDefinitions.Instance.ItemsByName[name])],
                    CheckedLocations = [.. CheckedLocations.Select(name => GameDefinitions.Instance.LocationsByName[name])],
                    ActiveAuraEffects = ActiveAuraEffects,
                    LocationCheckAttemptsThisStep = LocationCheckAttemptsThisStep,
                    PrngState = PrngState,
                };
            }
        }
    }

    private readonly AsyncEvent<GameStateEventArgs> _stateChangedEvent = new();

    private readonly AsyncEvent<StepStartedEventArgs> _stepStartedEvent = new();

    private readonly AsyncEvent<StepFinishedEventArgs> _stepFinishedEvent = new();

    private readonly Player _player = new();

    private readonly SemaphoreSlim _mutex = new(1, 1);

    private readonly TimeSpan _minInterval;

    private readonly TimeSpan _maxInterval;

    private readonly AutopelagoClient _client;

    private readonly TimeProvider _timeProvider;

    private State? _state;

    private ReceivedItemsEventArgs? _awaitingItems;

    private Prng.State _intervalPrngState = Prng.State.Start((ulong)Random.Shared.NextInt64(long.MinValue, long.MaxValue));

    private long _prevStartTimestamp;

    private TimeSpan _nextFullInterval;

    private DateTime? _lastBlockedReportUtc;

    public Game(TimeSpan minInterval, TimeSpan maxInterval, AutopelagoClient client, TimeProvider timeProvider)
    {
        _minInterval = minInterval;
        _maxInterval = maxInterval;
        _client = client;
        _client.ReceivedItems += OnReceivedItemsAsync;
        _timeProvider = timeProvider;
        _prevStartTimestamp = _timeProvider.GetTimestamp();
    }

    public State? CurrentState => _state;

    public event AsyncEventHandler<GameStateEventArgs> StateChanged
    {
        add { _stateChangedEvent.Add(value); }
        remove { _stateChangedEvent.Remove(value); }
    }

    public event AsyncEventHandler<StepStartedEventArgs> StepStarted
    {
        add { _stepStartedEvent.Add(value); }
        remove { _stepStartedEvent.Remove(value); }
    }

    public event AsyncEventHandler<StepFinishedEventArgs> StepFinished
    {
        add { _stepFinishedEvent.Add(value); }
        remove { _stepFinishedEvent.Remove(value); }
    }

    public async ValueTask<State> AdvanceFirstAsync(State initialState, CancellationToken cancellationToken)
    {
        if (_state is not null)
        {
            throw new InvalidOperationException("Already running.");
        }

        _state = initialState;
        _nextFullInterval = NextInterval(_state);
        if (_awaitingItems is ReceivedItemsEventArgs awaitingItems)
        {
            ulong prevEpoch = _state.Epoch;
            Handle(ref _state, awaitingItems);
            if (_state.Epoch != prevEpoch)
            {
                await _stateChangedEvent.InvokeAsync(this, new() { CurrentState = _state }, cancellationToken);
            }
        }

        return _state.IsCompleted
            ? _state
            : await AdvanceOnceAsync(cancellationToken);
    }

    public async ValueTask<State> AdvanceOnceAsync(CancellationToken cancellationToken)
    {
        if (_state is null)
        {
            throw new InvalidOperationException("Must call AdvanceFirstAsync first.");
        }

        TimeSpan remaining = _nextFullInterval - _timeProvider.GetElapsedTime(_prevStartTimestamp);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, _timeProvider, cancellationToken);
        }

        State prevState, nextState;
        _prevStartTimestamp = _timeProvider.GetTimestamp();
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            _nextFullInterval = NextInterval(_state);
            StepStartedEventArgs stepStarted = new()
            {
                CurrentState = prevState = _state,
            };
            await _stepStartedEvent.InvokeAsync(this, stepStarted, cancellationToken);

            StepFinishedEventArgs stepFinished = new()
            {
                StateBeforeAdvance = prevState,
                CurrentState = _state = nextState = _player.Advance(prevState),
            };
            await _stepFinishedEvent.InvokeAsync(this, stepFinished, cancellationToken);

            if (prevState.Epoch == nextState.Epoch)
            {
                DateTime utcNow = _timeProvider.GetUtcNow().UtcDateTime;
                if (!(_lastBlockedReportUtc + TimeSpan.FromMinutes(5) > utcNow))
                {
                    await _client.SendMessageAsync("Blocked!", cancellationToken);
                    _lastBlockedReportUtc = _timeProvider.GetUtcNow().UtcDateTime;
                }

                return nextState;
            }

            _lastBlockedReportUtc = null;
            await _stateChangedEvent.InvokeAsync(this, stepFinished, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }

        if (nextState.CheckedLocations.Count > prevState.CheckedLocations.Count)
        {
            await _client.SendLocationChecksAsync(nextState.CheckedLocations.Except(prevState.CheckedLocations), cancellationToken);
        }

        return nextState;
    }

    private async ValueTask OnReceivedItemsAsync(object? sender, ReceivedItemsEventArgs args, CancellationToken cancellationToken)
    {
        if (_state is null)
        {
            _awaitingItems = args;
            return;
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ulong prevEpoch = _state.Epoch;
            Handle(ref _state, args);
            if (_state.Epoch != prevEpoch)
            {
                await _stateChangedEvent.InvokeAsync(this, new() { CurrentState = _state }, cancellationToken);
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    private void Handle(ref State state, ReceivedItemsEventArgs args)
    {
        for (int i = args.Index; i < state.ReceivedItems.Count; i++)
        {
            if (args.Items[i - args.Index] != state.ReceivedItems[i])
            {
                throw new NotImplementedException("Need to resync.");
            }
        }

        ImmutableArray<ItemDefinitionModel> newItems = args.Items[(state.ReceivedItems.Count - args.Index)..];
        if (!newItems.IsEmpty)
        {
            state = state with
            {
                ReceivedItems = state.ReceivedItems.AddRange(newItems),
                ActiveAuraEffects = state.ActiveAuraEffects.AddRange(newItems.SelectMany(i => i.AurasGranted.Select(AuraEffect.Parse))),
            };
        }
    }

    private TimeSpan NextInterval(State state)
    {
        TimeSpan range = _maxInterval - _minInterval;
        TimeSpan baseInterval = _minInterval + (range * Prng.NextDouble(ref _intervalPrngState));
        return baseInterval * state.IntervalDurationMultiplier;
    }
}
