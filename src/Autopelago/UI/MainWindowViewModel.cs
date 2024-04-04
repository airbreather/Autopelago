using System.Collections.ObjectModel;
using System.Reactive;

using Avalonia.Platform.Storage;
using Avalonia.Threading;

using ReactiveUI;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Autopelago.UI;

public sealed class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
    {
        OpenSettingsCommand = ReactiveCommand.CreateFromTask(OpenSettingsAsync);
    }

    public ObservableCollection<GameStateViewModel> Games { get; } = [];

    private GameStateViewModel? _currentGame;
    public GameStateViewModel? CurrentGame
    {
        get => _currentGame;
        set => this.RaiseAndSetIfChanged(ref _currentGame, value);
    }

    public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }

    private async Task OpenSettingsAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            MainWindow topLevel = App.Current!.MainWindow!;
            IStorageProvider storageProvider = topLevel.StorageProvider;
            IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(new()
            {
                Title = "Select game settings",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new("YAML Files (*.yml, *.yaml)") { Patterns = ["*.yml", "*.yaml"] },
                    FilePickerFileTypes.All,
                ],
            });

            if (files is not [IStorageFile file])
            {
                return;
            }

            string settingsYaml;
            await using (Stream stream = await file.OpenReadAsync())
            {
                using StreamReader reader = new(stream, leaveOpen: true);
                settingsYaml = await reader.ReadToEndAsync();
            }

            AutopelagoSettingsModel settings = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Deserialize<AutopelagoSettingsModel>(settingsYaml);

            Games.Clear();
            CurrentGame = null;
            foreach (AutopelagoPlayerSettingsModel slot in settings.Slots)
            {
                Games.Add(new GameStateViewModel(slot.Name));
                CurrentGame ??= Games[^1];
            }
        });
    }
}
