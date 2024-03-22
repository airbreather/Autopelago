using System.Runtime.ExceptionServices;

namespace Autopelago;

public static class SyncOverAsync
{
    public static event EventHandler<ExceptionDispatchInfo>? BackgroundException;

    public static async void FireAndForget(Func<ValueTask> callback)
    {
        try
        {
            await callback();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            BackgroundException?.Invoke(null, ExceptionDispatchInfo.Capture(ex));
        }
    }
}
