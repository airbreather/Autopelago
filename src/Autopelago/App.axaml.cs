using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Autopelago.ViewModels;
using Autopelago.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace Autopelago;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppState))]
internal sealed partial class AppStateSerializerContext : JsonSerializerContext;

public sealed record AppState
{
    public Settings SlotSettings { get; init; } =
        Design.IsDesignMode
            ? Settings.ForDesigner
            : Settings.Default;

    public WindowState? MainWindowState { get; set; }

    public int? MainWindowWidth { get; set; }

    public int? MainWindowHeight { get; set; }

    public int? MainWindowPositionX { get; set; }

    public int? MainWindowPositionY { get; set; }

    [JsonIgnore]
    public PixelPoint? MainWindowPosition
    {
        get => (MainWindowPositionX, MainWindowPositionY) switch
        {
            (int x, int y) => new(x, y),
            _ => null,
        };
        set => (MainWindowPositionX, MainWindowPositionY) = (value?.X, value?.Y);
    }

    [JsonIgnore]
    public PixelSize? MainWindowSize
    {
        get => (MainWindowWidth, MainWindowHeight) switch
        {
            (int width, int height) => new(width, height),
            _ => null,
        };
        set => (MainWindowWidth, MainWindowHeight) = (value?.Width, value?.Height);
    }
}

public sealed partial class App : Application
{
    private static readonly FileStreamOptions s_readAsyncOptions = new()
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.ReadWrite | FileShare.Delete,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
    };

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static new App? Current => (App?)Application.Current;

    public MainWindowView? MainWindow { get; private set; }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        MainWindowViewModel mainWindowViewModel = new();
        MainWindow = new() { DataContext = mainWindowViewModel };

        JsonTypeInfo<AppState> typeInfo = (JsonTypeInfo<AppState>)AppStateSerializerContext.Default.GetTypeInfo(typeof(AppState))!;
        FileInfo? settingsFile = null;
        AppState initialState = new();
        if (!Design.IsDesignMode)
        {
            settingsFile = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autopelago", "lastSettings.json"));
            try
            {
                settingsFile.Directory!.Create();
                await using FileStream settingsStream = settingsFile.Open(s_readAsyncOptions);
                initialState = JsonSerializer.Deserialize(settingsStream, typeInfo) ?? throw new JsonException();
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
            }
        }

        if (MainWindow.Screens.ScreenFromWindow(MainWindow) is Screen screen)
        {
            if (initialState.MainWindowState is WindowState.Minimized or WindowState.FullScreen)
            {
                initialState.MainWindowState = null;
            }

            if (!((initialState.MainWindowPosition, initialState.MainWindowSize) is (PixelPoint testPosition, PixelSize testSize) &&
                  screen.Bounds.Contains(new PixelRect(testPosition, testSize))))
            {
                initialState.MainWindowPosition = null;
                initialState.MainWindowSize = null;
            }
        }

        mainWindowViewModel.SettingsSelection.SettingsModel = initialState.SlotSettings;
        if ((initialState.MainWindowPosition, initialState.MainWindowSize) is (PixelPoint initialPosition, PixelSize initialSize))
        {
            MainWindow.Position = initialPosition;
            MainWindow.Width = initialSize.Width;
            MainWindow.Height = initialSize.Height;
        }

        desktop.MainWindow = MainWindow;

        if (settingsFile is not null)
        {
            FileInfo tmpSettingsFile = new(Path.Combine(settingsFile.DirectoryName!, "tmp.json"));
            desktop.ShutdownRequested += OnShutdownRequested;
            void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs args)
            {
                AppState finalState = new()
                {
                    SlotSettings = mainWindowViewModel.SettingsSelection.SettingsModel,
                    MainWindowState = MainWindow.WindowState,
                    MainWindowPosition = MainWindow.Position,
                    MainWindowWidth = (int)MainWindow.ClientSize.Width,
                    MainWindowHeight = (int)MainWindow.ClientSize.Height,
                };

                try
                {
                    using (FileStream settingsStream = tmpSettingsFile.OpenWrite())
                    {
                        JsonSerializer.Serialize(settingsStream, finalState, typeInfo);
                    }

                    tmpSettingsFile.MoveTo(settingsFile.FullName, overwrite: true);
                }
                catch (IOException)
                {
                }
                catch (JsonException)
                {
                }
            }
        }
    }
}
