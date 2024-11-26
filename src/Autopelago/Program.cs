using Avalonia;
using Avalonia.ReactiveUI;
using Avalonia.Svg.Skia;

using Serilog;

namespace Autopelago;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .Enrich.FromLogContext()
            .CreateLogger();

        // get this built right away. errors here can be really annoying otherwise.
        GameDefinitions defs = GameDefinitions.Instance;
        if (args.FirstOrDefault() is "g")
        {
            Task.Run(async () => await DataCollector.RunAsync(Prng.State.Start(8675309), default)).GetAwaiter().GetResult();
            return 0;
        }

        return BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        GC.KeepAlive(typeof(SvgImageExtension).Assembly);
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
    }
}
