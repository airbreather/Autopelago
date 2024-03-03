using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using ArchipelagoClientDotNet;

public sealed record NextStepStartedEventArgs
{
    public required Game.State StateBeforeAdvance { get; init; }
}

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Lifetime is too close to the application's lifetime for me to care right now.")]
public sealed class Game
{
    public sealed record State
    {
        public State()
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

        public int DiceModifier => (RatCount / 3) - (LocationCheckAttemptsThisStep * 5);

        public int RatCount => ReceivedItems.Sum(i => i.RatCount).GetValueOrDefault();

        public static State Start(Random? random = null)
        {
            return new()
            {
                CurrentLocation = GameDefinitions.Instance.StartLocation,
                TargetLocation = GameDefinitions.Instance.StartLocation,
                ReceivedItems = [],
                CheckedLocations = [],
                LocationCheckAttemptsThisStep = 0,
                PrngState = Prng.State.Start(random),
            };
        }

        public static int NextD20(ref State state)
        {
            return ((int)Math.Floor(NextDouble(ref state) * 20)) + 1;
        }

        public static double NextDouble(ref State state)
        {
            Prng.State s = state.PrngState;
            double result;
            do
            {
                result = Prng.NextDouble(ref s);
            } while (result == 1); // it's unbelievably unlikely, but if I want to make this method perfect, then I will.

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

    private readonly IAutopelagoClient _client;

    private readonly TimeProvider _timeProvider;

    private readonly SemaphoreSlim _mutex = new(1, 1);

    private readonly AsyncEvent<NextStepStartedEventArgs> _nextStepStarted = new();

    private readonly Player _player = new();

    private GameStateStorage? _stateStorage;

    private State _state = State.Start(new Random(123));

    public Game(IAutopelagoClient client, TimeProvider timeProvider)
    {
        _client = client;
        _timeProvider = timeProvider;
    }

    public event AsyncEventHandler<NextStepStartedEventArgs> NextStepStarted
    {
        add => _nextStepStarted.Add(value);
        remove => _nextStepStarted.Remove(value);
    }

    public async ValueTask RunUntilCanceledAsync(GameStateStorage stateStorage, CancellationToken cancellationToken)
    {
        if (_stateStorage is not null)
        {
            throw new InvalidOperationException("Already called this method before.");
        }

        _stateStorage = stateStorage;
        if (await stateStorage.LoadAsync(cancellationToken) is State initialState)
        {
            _state = initialState;
        }

        _client.ReceivedItems += OnClientReceivedItemsAsync;

        while (true)
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
            }
            finally
            {
                _mutex.Release();
            }

            if (stateBeforeAdvance.Epoch == stateAfterAdvance.Epoch)
            {
                continue;
            }

            await _stateStorage.SaveAsync(stateAfterAdvance, cancellationToken);
            if (stateBeforeAdvance.CheckedLocations.Count < stateAfterAdvance.CheckedLocations.Count)
            {
                await _client.SendLocationChecksAsync(stateAfterAdvance.CheckedLocations.Except(stateBeforeAdvance.CheckedLocations), cancellationToken);
            }

            _state = stateAfterAdvance;
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

            List<ItemDefinitionModel> newItems = [.. args.Items.Except(_state.ReceivedItems)];
            if (newItems.Count > 0)
            {
                _state = _state with { ReceivedItems = _state.ReceivedItems.AddRange(newItems) };
            }
        }
        finally
        {
            _mutex.Release();
        }
    }
}
