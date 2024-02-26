using System.Runtime.ExceptionServices;

namespace ArchipelagoClientDotNet;

public sealed class BackgroundExceptionEventArgs : EventArgs
{
    public required ExceptionDispatchInfo BackgroundException { get; init; }
}

public static class BackgroundTaskRunner
{
    public static event EventHandler<BackgroundExceptionEventArgs>? BackgroundException;

    public static Task Run(Func<ValueTask> callback, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            try
            {
                await callback().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                BackgroundException?.Invoke(null, new() { BackgroundException = ExceptionDispatchInfo.Capture(ex) });
                throw;
            }
        }, cancellationToken);
    }
}
