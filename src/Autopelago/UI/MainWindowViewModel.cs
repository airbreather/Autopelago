using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;

using Avalonia.Platform.Storage;
using Avalonia.Threading;

using DynamicData;

using ReactiveUI;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Autopelago.UI;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly SourceList<GameStateViewModel> _games = new();

    private readonly IDisposable _gamesSubscription;

    private bool _disposed;

    public MainWindowViewModel()
    {
        OpenSettingsCommand = ReactiveCommand.CreateFromTask(OpenSettingsAsync);

        _gamesSubscription = _games.Connect()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out ReadOnlyObservableCollection<GameStateViewModel> gamesReadOnly)
            .Subscribe();
        Games = gamesReadOnly;
    }

    public ReadOnlyObservableCollection<GameStateViewModel> Games { get; }

    private GameStateViewModel? _currentGame;
    public GameStateViewModel? CurrentGame
    {
        get => _currentGame;
        set => this.RaiseAndSetIfChanged(ref _currentGame, value);
    }

    public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gamesSubscription.Dispose();
        _games.Dispose();
        _disposed = true;
    }

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

            CurrentGame = null;
            GameStateViewModel? newCurrentGame = null;
            _games.Edit(games =>
            {
                games.Clear();
                foreach (AutopelagoPlayerSettingsModel slot in settings.Slots)
                {
                    games.Add(new GameStateViewModel(slot.Name));
                    newCurrentGame ??= games[^1];
                }
            });
            CurrentGame = newCurrentGame;
        });
    }
}
