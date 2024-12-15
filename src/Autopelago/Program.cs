using Autopelago.Infrastructure;

using Avalonia;

using Spectre.Console.Cli;

namespace Autopelago;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        CommandApp<GameCommand> app = new();
        app.Configure(cfg =>
        {
            cfg.AddCommand<SimulateCommand>("simulate")
                .IsHidden();
        });
        return app.Run(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() => GameCommand.BuildAvaloniaApp(new());
}
