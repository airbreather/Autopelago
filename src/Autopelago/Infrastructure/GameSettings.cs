using System.ComponentModel;

using Spectre.Console.Cli;

namespace Autopelago.Infrastructure;

internal sealed partial class GameSettings : CommandSettings
{
    [CommandOption("--no-gpu")]
    [Description("Disables [bold]all[/] GPU acceleration. Only set this if you run into problems, and only as a last resort (try the other flags first!).")]
    public bool? ForceSoftwareRendering { get; init; }
}
