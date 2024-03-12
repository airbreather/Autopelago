using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using ArchipelagoClientDotNet;

namespace Autopelago;

public sealed record NextStepStartedEventArgs
{
    public required Game.State StateBeforeAdvance { get; init; }
}

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Lifetime is too close to the application's lifetime for me to care right now.")]
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

        public bool IsCompleted => ReceivedItems.Contains(GameDefinitions.Instance.GoalItem);

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

    private readonly AutopelagoClient _client;

    private readonly TimeProvider _timeProvider;

    private readonly GameStateStorage _stateStorage;

    private readonly SemaphoreSlim _mutex = new(1, 1);

    private readonly AsyncEvent<NextStepStartedEventArgs> _nextStepStarted = new();

    private readonly Player _player = new();

    private State _state;

    public Game(AutopelagoClient client, TimeProvider timeProvider, GameStateStorage stateStorage, Random? random = null)
    {
        _client = client;
        _timeProvider = timeProvider;
        _stateStorage = stateStorage;
        _state = State.Start(random);
    }

    public State CurrentState => _state;

    public event AsyncEventHandler<NextStepStartedEventArgs> NextStepStarted
    {
        add => _nextStepStarted.Add(value);
        remove => _nextStepStarted.Remove(value);
    }

    public async ValueTask RunUntilCanceledOrCompletedAsync(CancellationToken cancellationToken)
    {
        if (await _stateStorage.LoadAsync(cancellationToken) is State initialState)
        {
            _state = initialState;
        }

        _client.ReceivedItems += OnClientReceivedItemsAsync;

        while (!_state.IsCompleted)
        {
            State stateBeforeAdvance;
            State stateAfterAdvance;

            // TODO: more control over the delay
            await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken);
            await _mutex.WaitAsync(cancellationToken);
            try
            {
                stateBeforeAdvance = _state;
                await _nextStepStarted.InvokeAsync(this, new() { StateBeforeAdvance = stateBeforeAdvance }, cancellationToken);
                stateAfterAdvance = _player.Advance(stateBeforeAdvance);
                if (stateBeforeAdvance.Epoch == stateAfterAdvance.Epoch)
                {
                    continue;
                }

                _state = stateAfterAdvance;
                await _stateStorage.SaveAsync(stateAfterAdvance, cancellationToken);
            }
            finally
            {
                _mutex.Release();
            }

            if (stateBeforeAdvance.CheckedLocations.Count < stateAfterAdvance.CheckedLocations.Count)
            {
                await _client.SendLocationChecksAsync(stateAfterAdvance.CheckedLocations.Except(stateBeforeAdvance.CheckedLocations), cancellationToken);
            }
        }
    }

    private async ValueTask OnClientReceivedItemsAsync(object? sender, ReceivedItemsEventArgs args, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        await _mutex.WaitAsync(cancellationToken);
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
            if (newItems.Length > 0)
            {
                _state = _state with { ReceivedItems = _state.ReceivedItems.AddRange(newItems) };
                await _stateStorage.SaveAsync(_state, cancellationToken);
            }
        }
        finally
        {
            _mutex.Release();
        }
    }
}
