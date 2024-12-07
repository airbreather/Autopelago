using Avalonia;
using Avalonia.ReactiveUI;
using Avalonia.Rendering.Composition;
using Avalonia.Svg.Skia;

using Serilog;

namespace Autopelago;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "g")
        {
            if (args.Length != 6)
            {
                Console.Error.WriteLine("When the first arg is 'g', there need to be 6 total args");
                return 1;
            }

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .Enrich.FromLogContext()
                .CreateLogger();

            Task.Run(async () => await DataCollector.RunAsync(args[1], int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), Prng.State.Start(ulong.Parse(args[5])), default)).GetAwaiter().GetResult();
            return 0;
        }

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
            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        GC.KeepAlive(typeof(SvgImageExtension).Assembly);
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                RenderingMode = [X11RenderingMode.Vulkan, X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software],

                // the default behavior here seems completely borked when composition rendering uses
                // region-based dirty rects.
                UseRetainedFramebuffer = true,
            })
            .With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Vulkan, Win32RenderingMode.Wgl, Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
            })
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = [AvaloniaNativeRenderingMode.Metal, AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software],
            })
            .With(new CompositionOptions
            {
                UseRegionDirtyRectClipping = true,
            })
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
    }
}
