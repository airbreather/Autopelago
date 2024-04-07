using ReactiveUI;

namespace Autopelago.ViewModels;

public sealed class CollectableItemViewModel : ViewModelBase
{
    public required string ItemKey { get; init; }

    public required ItemDefinitionModel Model { get; init; }

    private bool _collected;

    public bool Collected
    {
        get => _collected;
        set => this.RaiseAndSetIfChanged(ref _collected, value);
    }
}
