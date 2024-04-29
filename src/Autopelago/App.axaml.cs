using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Autopelago.ViewModels;
using Autopelago.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

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

    public PixelRectProxy? MainWindowBounds { get; set; }

    public ImmutableArray<PixelRectProxy>? BasisScreens { get; set; }
}

public readonly record struct PixelRectProxy(int X, int Y, int Width, int Height)
{
    public static implicit operator PixelRect(PixelRectProxy copyFrom) => new(copyFrom.X, copyFrom.Y, copyFrom.Width, copyFrom.Height);

    public static implicit operator Rect(PixelRectProxy copyFrom) => new(copyFrom.X, copyFrom.Y, copyFrom.Width, copyFrom.Height);

    public static implicit operator PixelRectProxy(PixelRect copyFrom) => new(copyFrom.X, copyFrom.Y, copyFrom.Width, copyFrom.Height);
    public static explicit operator PixelRectProxy(Rect copyFrom) => new((int)copyFrom.X, (int)copyFrom.Y, (int)copyFrom.Width, (int)copyFrom.Height);
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

        // don't start minimized or full-screen
        if (initialState.MainWindowState is WindowState.Minimized or WindowState.FullScreen)
        {
            initialState.MainWindowState = null;
        }

        // only restore bounds if the same screens intersect it now.
        PixelSize size = new(800, 600);
        if (initialState.MainWindowBounds is PixelRectProxy prevBoundsProxy)
        {
            PixelRect prevBounds = prevBoundsProxy;
            size = prevBounds.Size;

            ImmutableArray<PixelRectProxy> currScreens = [.. MainWindow.Screens.Intersecting(prevBounds)];
            if (!currScreens.SequenceEqual(initialState.BasisScreens ?? []))
            {
                initialState.MainWindowBounds = null;
            }
        }

        mainWindowViewModel.SettingsSelection.SettingsModel = initialState.SlotSettings;
        if (initialState.MainWindowBounds is PixelRectProxy initialBoundsProxy)
        {
            PixelRect initialBounds = initialBoundsProxy;
            MainWindow.Position = initialBounds.Position;
        }

        MainWindow.Width = size.Width;
        MainWindow.Height = size.Height;

        desktop.MainWindow = MainWindow;

        if (settingsFile is not null)
        {
            FileInfo tmpSettingsFile = new(Path.Combine(settingsFile.DirectoryName!, "tmp.json"));
            desktop.ShutdownRequested += OnShutdownRequested;
            void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs args)
            {
                PixelRect bounds = new(MainWindow.Position, new PixelSize((int)MainWindow.Width, (int)MainWindow.Height));
                AppState finalState = new()
                {
                    SlotSettings = mainWindowViewModel.SettingsSelection.SettingsModel,
                    MainWindowState = MainWindow.WindowState,
                    MainWindowBounds = bounds,
                    BasisScreens = [.. MainWindow.Screens.Intersecting(bounds)],
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
