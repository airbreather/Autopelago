using System.Runtime.InteropServices;

using Spectre.Console.Cli;

namespace Autopelago.Infrastructure;

public abstract class CancelableAsyncCommand<T> : AsyncCommand<T>
    where T : CommandSettings
{
    public sealed override async Task<int> ExecuteAsync(CommandContext context, T settings)
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()))
        {
            return await ExecuteAsync(context, settings, CancellationToken.None);
        }

        // https://github.com/dotnet/runtime/blob/v9.0.0/src/libraries/Microsoft.Extensions.Hosting/src/Internal/ConsoleLifetime.netcoreapp.cs
        using CancellationTokenSource cts = new();
        using PosixSignalRegistration reg1 = PosixSignalRegistration.Create(PosixSignal.SIGINT, HandleSignal);
        using PosixSignalRegistration reg2 = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, HandleSignal);
        using PosixSignalRegistration reg3 = PosixSignalRegistration.Create(PosixSignal.SIGTERM, HandleSignal);
        void HandleSignal(PosixSignalContext signalContext)
        {
            signalContext.Cancel = true;
            cts.Cancel();
        }

        return await ExecuteAsync(context, settings, cts.Token);
    }

    public abstract Task<int> ExecuteAsync(CommandContext context, T settings, CancellationToken cancellationToken);
}
