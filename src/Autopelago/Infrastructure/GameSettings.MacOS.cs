using System.ComponentModel;

using Avalonia;

using Spectre.Console.Cli;

namespace Autopelago.Infrastructure;

internal sealed partial class GameSettings
{
    [Description("Disables Metal-based GPU acceleration. Only set this if you run into problems.")]
    [CommandOption("--no-metal")]
    public bool? NoMetal { get; init; }

    public AvaloniaNativePlatformOptions GetPlatformOptions()
    {
        List<AvaloniaNativeRenderingMode> renderingModes = [AvaloniaNativeRenderingMode.Metal, AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software];
        if (ForceSoftwareRendering == true)
        {
            renderingModes.RemoveAll(m => m != AvaloniaNativeRenderingMode.Software);
        }
        else
        {
            if (NoMetal == true)
            {
                renderingModes.Remove(AvaloniaNativeRenderingMode.Metal);
            }
        }

        return new()
        {
            RenderingMode = renderingModes,
        };
    }
}
