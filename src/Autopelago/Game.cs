using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autopelago;

public sealed record StepStartedEventArgs
{
    public required Game.State StateBeforeAdvance { get; init; }
}

public sealed record StepFinishedEventArgs
{
    public required Game.State StateBeforeAdvance { get; init; }

    public required Game.State StateAfterAdvance { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Game.State.Proxy))]
internal sealed partial class GameStateProxySerializerContext : JsonSerializerContext
{
}

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
            CurrentLocation = copyFrom.CurrentLocation;
            TargetLocation = copyFrom.TargetLocation;
            ReceivedItems = copyFrom.ReceivedItems;
            CheckedLocations = copyFrom.CheckedLocations;
            PrngState = copyFrom.PrngState;
            LocationCheckAttemptsThisStep = copyFrom.LocationCheckAttemptsThisStep;
        }

        public ulong Epoch { get; private init; }

        public required LocationDefinitionModel CurrentLocation { get; init; }

        public required LocationDefinitionModel TargetLocation { get; init; }

        public required ImmutableList<ItemDefinitionModel> ReceivedItems { get; init; }

        public required ImmutableList<LocationDefinitionModel> CheckedLocations { get; init; }

        public required int LocationCheckAttemptsThisStep { get; init; }

        public required Prng.State PrngState { get; init; }

        public bool IsCompleted => CurrentLocation == GameDefinitions.Instance.GoalLocation;

        public int DiceModifier => (RatCount / 3) - (LocationCheckAttemptsThisStep * 5);

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
                CurrentLocation = GameDefinitions.Instance.StartLocation,
                TargetLocation = GameDefinitions.Instance.StartLocation,
                ReceivedItems = [],
                CheckedLocations = [],
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
                CurrentLocation = CurrentLocation.Name,
                TargetLocation = TargetLocation.Name,
                ReceivedItems = [.. ReceivedItems.Select(i => i.Name)],
                CheckedLocations = [.. CheckedLocations.Select(l => l.Name)],
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
                CurrentLocation == other.CurrentLocation &&
                TargetLocation == other.TargetLocation &&
                LocationCheckAttemptsThisStep == other.LocationCheckAttemptsThisStep &&
                ReceivedItems.SequenceEqual(other.ReceivedItems) &&
                CheckedLocations.SequenceEqual(other.CheckedLocations);
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

            public required string CurrentLocation { get; init; }

            public required string TargetLocation { get; init; }

            public required ImmutableArray<string> ReceivedItems { get; init; }

            public required ImmutableArray<string> CheckedLocations { get; init; }

            public required int LocationCheckAttemptsThisStep { get; init; }

            public Prng.State PrngState { get; init; }

            public State ToState()
            {
                return new()
                {
                    Epoch = Epoch,
                    CurrentLocation = GameDefinitions.Instance.LocationsByName[CurrentLocation],
                    TargetLocation = GameDefinitions.Instance.LocationsByName[TargetLocation],
                    ReceivedItems = [.. ReceivedItems.Select(name => GameDefinitions.Instance.ItemsByName[name])],
                    CheckedLocations = [.. CheckedLocations.Select(name => GameDefinitions.Instance.LocationsByName[name])],
                    LocationCheckAttemptsThisStep = LocationCheckAttemptsThisStep,
                    PrngState = PrngState,
                };
            }
        }
    }

    private readonly AsyncEvent<StepStartedEventArgs> _stepStartedEvent = new();

    private readonly AsyncEvent<StepFinishedEventArgs> _stepFinishedEvent = new();

    private readonly TimeSpan _minInterval;

    private readonly TimeSpan _maxInterval;

    private readonly AutopelagoClient _client;

    private readonly TimeProvider _timeProvider;

    private State? _state;

    private Prng.State _intervalPrngState = Prng.State.Start((ulong)Random.Shared.NextInt64(long.MinValue, long.MaxValue));

    public Game(TimeSpan minInterval, TimeSpan maxInterval, AutopelagoClient client, TimeProvider timeProvider)
    {
        _minInterval = minInterval;
        _maxInterval = maxInterval;
        _client = client;
        _timeProvider = timeProvider;
    }

    public State CurrentState => _state ?? throw new InvalidOperationException("Not running yet.");

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

    public CancellationTokenSource RunGameLoop(State initialState)
    {
        if (_state is not null)
        {
            throw new InvalidOperationException("Already running.");
        }

        _state = initialState;
        CancellationTokenSource cts = new();
        SemaphoreSlim mutex = new(1, 1);
        SyncOverAsync.FireAndForget(async () => await GameLoopAsync(cts.Token));
        _client.ReceivedItems += OnReceivedItemsAsync;
        return cts;

        async ValueTask GameLoopAsync(CancellationToken cancellationToken)
        {
            Player player = new();
            Task nextDelay = Task.Delay(NextInterval(), _timeProvider, cancellationToken);
            while (true)
            {
                await nextDelay;
                nextDelay = Task.Delay(NextInterval(), _timeProvider, cancellationToken);
                State prevState, nextState;
                await mutex.WaitAsync(cancellationToken);
                try
                {
                    StepStartedEventArgs stepStarted = new()
                    {
                        StateBeforeAdvance = prevState = _state,
                    };
                    await _stepStartedEvent.InvokeAsync(this, stepStarted, cancellationToken);

                    StepFinishedEventArgs stepFinished = new()
                    {
                        StateBeforeAdvance = _state,
                        StateAfterAdvance = nextState = player.Advance(prevState),
                    };
                    await _stepFinishedEvent.InvokeAsync(this, stepFinished, cancellationToken);
                }
                finally
                {
                    mutex.Release();
                }

                if (prevState.Epoch == nextState.Epoch)
                {
                    continue;
                }

                await _client.SendLocationChecksAsync(nextState.CheckedLocations.Except(prevState.CheckedLocations), cancellationToken);
                if (nextState.IsCompleted)
                {
                    break;
                }
            }
        }

        async ValueTask OnReceivedItemsAsync(object? sender, ReceivedItemsEventArgs args, CancellationToken cancellationToken)
        {
            using CancellationTokenSource cts2 = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
            await mutex.WaitAsync(cts2.Token);
            try
            {
                for (int i = args.Index; i < _state.ReceivedItems.Count; i++)
                {
                    if (args.Items[i - args.Index] != _state.ReceivedItems[i])
                    {
                        throw new NotImplementedException("Need to resync.");
                    }
                }

                ImmutableArray<ItemDefinitionModel> newItems = args.Items[(_state.ReceivedItems.Count - args.Index)..];
                if (newItems.Length == 0)
                {
                    return;
                }

                _state = _state with { ReceivedItems = _state.ReceivedItems.AddRange(newItems) };
            }
            finally
            {
                mutex.Release();
            }
        }
    }

    private TimeSpan NextInterval()
    {
        TimeSpan range = _maxInterval - _minInterval;
        return _minInterval + (range * Prng.NextDouble(ref _intervalPrngState));
    }
}
