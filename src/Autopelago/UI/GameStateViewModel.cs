using System.Collections.Frozen;
using System.Collections.Immutable;

using Avalonia.Threading;

using ReactiveUI;

namespace Autopelago.UI;

public sealed class GameStateViewModel : ViewModelBase
{
    private readonly FrozenDictionary<ItemDefinitionModel, CollectableItemViewModel> _progressionItemLookup;

    public GameStateViewModel(string slotName)
    {
        SlotName = slotName;
        _progressionItemLookup = ProgressionItems.ToFrozenDictionary(vm => vm.Model);
    }

    public string SlotName { get; }

    private Game? _watchedGame;
    public Game? WatchedGame
    {
        get => _watchedGame;
        set
        {
            if (_watchedGame is Game prev)
            {
                prev.StateChanged -= OnGameStateChangedAsync;
            }

            _watchedGame = value;

            if (value is Game next)
            {
                next.StateChanged += OnGameStateChangedAsync;
            }
        }
    }

    private int _foodFactor;
    public int FoodFactor
    {
        get => _foodFactor;
        set => this.RaiseAndSetIfChanged(ref _foodFactor, value);
    }

    private int _luckFactor;
    public int LuckFactor
    {
        get => _luckFactor;
        set => this.RaiseAndSetIfChanged(ref _luckFactor, value);
    }

    private int _energyFactor;
    public int EnergyFactor
    {
        get => _energyFactor;
        set => this.RaiseAndSetIfChanged(ref _energyFactor, value);
    }

    private int _styleFactor;
    public int StyleFactor
    {
        get => _styleFactor;
        set => this.RaiseAndSetIfChanged(ref _styleFactor, value);
    }

    private int _distractionCounter;
    public int DistractionCounter
    {
        get => _distractionCounter;
        set => this.RaiseAndSetIfChanged(ref _distractionCounter, value);
    }

    private bool _hasConfidence;
    public bool HasConfidence
    {
        get => _hasConfidence;
        set => this.RaiseAndSetIfChanged(ref _hasConfidence, value);
    }

    public ImmutableArray<CollectableItemViewModel> ProgressionItems { get; } = GameDefinitions.Instance.ProgressionItems.Values
        .Select(item => new CollectableItemViewModel { Model = item })
        .ToImmutableArray();

    private async ValueTask OnGameStateChangedAsync(object? sender, GameStateEventArgs args, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            FoodFactor = args.CurrentState.FoodFactor;
            LuckFactor = args.CurrentState.LuckFactor;
            EnergyFactor = args.CurrentState.EnergyFactor;
            StyleFactor = args.CurrentState.StyleFactor;
            DistractionCounter = args.CurrentState.DistractionCounter;
            HasConfidence = args.CurrentState.HasConfidence;
            foreach (ItemDefinitionModel progressionItem in args.CurrentState.ReceivedItems)
            {
                if (_progressionItemLookup.TryGetValue(progressionItem, out CollectableItemViewModel? vm))
                {
                    vm.Collected = true;
                }
            }
        }, DispatcherPriority.Default, cancellationToken);
    }
}
