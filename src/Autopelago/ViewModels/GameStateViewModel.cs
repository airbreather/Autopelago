using System.Collections.Immutable;

using ReactiveUI;

namespace Autopelago.ViewModels;

public sealed class GameStateViewModel : ViewModelBase
{
    private string? _slotName;
    public string SlotName
    {
        get => _slotName ?? "";
        set => this.RaiseAndSetIfChanged(ref _slotName, value);
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

    public ImmutableArray<CollectableItemViewModel> ProgressionItems { get; } = GameDefinitions.Instance.ProgressionItems
        .Select(kvp => new CollectableItemViewModel { ItemKey = kvp.Key, Model = kvp.Value })
        .ToImmutableArray();
}
