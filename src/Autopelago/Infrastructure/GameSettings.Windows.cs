using Avalonia;

using Spectre.Console.Cli;

namespace Autopelago.Infrastructure;

internal sealed partial class GameSettings
{
    [Description("Disables Vulkan-based GPU acceleration. Only set this if you run into problems.")]
    [CommandOption("--no-vulkan")]
    public bool? NoVulkan { get; init; }

    [Description("Disables WGL-based GPU acceleration. Only set this if you run into problems.")]
    [CommandOption("--no-wgl")]
    public bool? NoWgl { get; init; }

    public Win32PlatformOptions GetPlatformOptions()
    {
        List<Win32RenderingMode> renderingModes = [Win32RenderingMode.Vulkan, Win32RenderingMode.Wgl, Win32RenderingMode.AngleEgl, Win32RenderingMode.Software];
        if (ForceSoftwareRendering == true)
        {
            renderingModes.RemoveAll(m => m != Win32RenderingMode.Software);
        }
        else
        {
            if (NoVulkan == true)
            {
                renderingModes.Remove(Win32RenderingMode.Vulkan);
            }

            if (NoWgl == true)
            {
                renderingModes.Remove(Win32RenderingMode.Wgl);
            }
        }

        return new()
        {
            RenderingMode = renderingModes,
        };
    }
}
