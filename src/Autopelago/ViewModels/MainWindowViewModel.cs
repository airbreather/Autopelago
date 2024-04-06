using ReactiveUI;

namespace Autopelago.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
    {
        SettingsSelection.ConnectCommand
            .Subscribe(_ => ContentViewModel = new GameStateViewModel());

        _contentViewModel = SettingsSelection;
    }

    public SettingsSelectionViewModel SettingsSelection { get; } = new();

    private ViewModelBase _contentViewModel;
    public ViewModelBase ContentViewModel
    {
        get => _contentViewModel;
        set => this.RaiseAndSetIfChanged(ref _contentViewModel, value);
    }
}
