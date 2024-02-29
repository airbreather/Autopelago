using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using ArchipelagoClientDotNet;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Lifetime is too close to the application's lifetime for me to care right now.")]
public sealed class Game
{
    public sealed record State
    {
        public ulong Epoch { get; init; }

        public int RatCount => ReceivedItems.Sum(i => i.RatCount).GetValueOrDefault();

        public ImmutableList<ItemDefinitionModel> ReceivedItems { get; init; } = [];

        public Prng.State PrngState { get; init; }

        public Proxy ToProxy()
        {
            return new()
            {
                Epoch = Epoch,
                ReceivedItems = [..ReceivedItems.Select(i => i.Name)],
                PrngState = PrngState,
            };
        }

        public sealed record Proxy
        {
            public ulong Epoch { get; init; }

            public ImmutableArray<string> ReceivedItems { get; init; }

            public Prng.State PrngState { get; init; }

            public State ToState()
            {
                return new()
                {
                    Epoch = Epoch,
                    ReceivedItems = [..ReceivedItems.Select(name => GameDefinitions.Items[name])],
                    PrngState = PrngState,
                };
            }
        }
    }

    private readonly IAutopelagoClient _client;

    private readonly TimeProvider _timeProvider;

    private readonly SemaphoreSlim _mutex = new(1, 1);

    private GameStateStorage? _stateStorage;

    private State _state;

    public Game(IAutopelagoClient client, TimeProvider timeProvider)
    {
        _client = client;
        _timeProvider = timeProvider;
        _state = new() { PrngState = Prng.State.Start(new Random(123)) };
    }

    public async ValueTask SetStateStorageAsync(GameStateStorage stateStorage, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        if (_stateStorage is not null)
        {
            throw new InvalidOperationException("Already called this method before.");
        }

        _stateStorage = stateStorage;
        if (await stateStorage.LoadAsync(cancellationToken) is State initialState)
        {
            _state = initialState;
        }
    }

    public async ValueTask RunUntilCanceledAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        if (_stateStorage is null)
        {
            throw new InvalidOperationException("Must set the state storage first.");
        }

        _client.ReceivedItems += OnClientReceivedItemsAsync;

        while (true)
        {
            // TODO: better
            await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken);

            await _mutex.WaitAsync(cancellationToken);
            try
            {
                State state = Advance();
                if (_state != state)
                {
                    // TODO: send packets as appropriate for how the state has changed

                    await _stateStorage.SaveAsync(state, cancellationToken);
                    _state = state;
                }
            }
            finally
            {
                _mutex.Release();
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
                if (_state.ReceivedItems[i] != _state.ReceivedItems[i - args.Index])
                {
                    throw new NotImplementedException("Need to resync.");
                }
            }

            if (_state.ReceivedItems.Count - args.Index == _state.ReceivedItems.Count)
            {
                return;
            }

            _state = _state with
            {
                Epoch = _state.Epoch + 1,
                ReceivedItems = _state.ReceivedItems.AddRange(args.Items.Skip(_state.ReceivedItems.Count - args.Index)),
            };
        }
        finally
        {
            _mutex.Release();
        }
    }

    private State Advance()
    {
        // TODO: actually everything here.
        return _state with
        {
            Epoch = _state.Epoch + 1,
        };
    }
}
