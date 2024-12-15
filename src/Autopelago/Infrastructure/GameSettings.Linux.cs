using System.ComponentModel;

using Avalonia;

using Spectre.Console.Cli;

namespace Autopelago.Infrastructure;

internal sealed partial class GameSettings
{
    [Description("Disables Vulkan-based GPU acceleration. Only set this if you run into problems.")]
    [CommandOption("--no-vulkan")]
    public bool? NoVulkan { get; init; }

    [Description("Disables EGL-based GPU acceleration. Only set this if you run into problems.")]
    [CommandOption("--no-egl")]
    public bool? NoEgl { get; init; }

    public X11PlatformOptions GetPlatformOptions()
    {
        List<X11RenderingMode> renderingModes = [X11RenderingMode.Vulkan, X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software];
        if (ForceSoftwareRendering == true)
        {
            renderingModes.RemoveAll(m => m != X11RenderingMode.Software);
        }
        else
        {
            if (NoVulkan == true)
            {
                renderingModes.Remove(X11RenderingMode.Vulkan);
            }

            if (NoEgl == true)
            {
                renderingModes.Remove(X11RenderingMode.Egl);
            }
        }

        return new()
        {
            RenderingMode = renderingModes,

            // the default behavior here seems completely borked when composition rendering uses
            // region-based dirty rects.
            UseRetainedFramebuffer = true,
        };
    }
}
