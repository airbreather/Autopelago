using Avalonia;
using Avalonia.ReactiveUI;
using Avalonia.Rendering.Composition;
using Avalonia.Svg.Skia;

using Serilog;

using Spectre.Console.Cli;

namespace Autopelago.Infrastructure;

internal sealed class GameCommand : Command<GameSettings>
{
    public override int Execute(CommandContext context, GameSettings settings)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Async(
                within => within.Console(),
                blockWhenFull: true
            )
            .Enrich.FromLogContext()
            .CreateLogger();

        // get this built right away. errors here can be really annoying otherwise.
        GameDefinitions defs = GameDefinitions.Instance;
        try
        {
            return BuildAvaloniaApp(settings)
                .StartWithClassicDesktopLifetime([.. context.Arguments]);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp(GameSettings settings)
    {
        GC.KeepAlive(typeof(SvgImageExtension).Assembly);
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(settings.GetPlatformOptions())
            .With(new CompositionOptions
            {
                UseRegionDirtyRectClipping = true,
            })
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
    }
}
