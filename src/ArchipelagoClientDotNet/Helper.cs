using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;

namespace ArchipelagoClientDotNet;

public static class Helper
{
    public static GetOffSyncContextAwaitableAndAwaiter ConfigureAwaitFalse() => default;

    // BEGIN: copy-pasted from Nerdbank.Streams@3ec3e25ba48f3d59fe8c1220563d8fe71a17b8d8
    // with modifications per dotnet/Nerdbank.Streams#719 and to make transplantation work better

    /// <summary>
    /// Enables efficiently reading a <see cref="WebSocket"/> using <see cref="PipeReader"/>.
    /// </summary>
    /// <param name="webSocket">The web socket to read from using a pipe.</param>
    /// <param name="sizeHint">A hint at the size of messages that are commonly transferred. Use 0 for a commonly reasonable default.</param>
    /// <param name="pipeOptions">Optional pipe options to use.</param>
    /// <param name="cancellationToken">A cancellation token that aborts reading from the <paramref name="webSocket"/>.</param>
    /// <returns>A <see cref="PipeReader"/>.</returns>
    public static PipeReader UsePipeReader(this WebSocket webSocket, int sizeHint = 0, PipeOptions? pipeOptions = null, CancellationToken cancellationToken = default)
{
        var pipe = new Pipe(pipeOptions ?? PipeOptions.Default);
        Task.Run(async delegate
        {
            while (true)
            {
                Memory<byte> memory = pipe.Writer.GetMemory(sizeHint);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ValueWebSocketReceiveResult readResult = await webSocket.ReceiveAsync(memory, cancellationToken).ConfigureAwait(false);
                    if (readResult.Count == 0)
                    {
                        break;
                    }

                    pipe.Writer.Advance(readResult.Count);
                }
                catch (Exception ex)
                {
                    // Propagate the exception to the reader.
                    await pipe.Writer.CompleteAsync(ex).ConfigureAwait(false);
                    return;
                }

                FlushResult result = await pipe.Writer.FlushAsync().ConfigureAwait(false);
                if (result.IsCompleted)
                {
                    break;
                }
            }

            // Tell the PipeReader that there's no more data coming
            await pipe.Writer.CompleteAsync().ConfigureAwait(false);
        }).Forget();

        return pipe.Reader;
    }

    /// <summary>
    /// Enables efficiently writing to a <see cref="WebSocket"/> using a <see cref="PipeWriter"/>.
    /// </summary>
    /// <param name="webSocket">The web socket to write to using a pipe.</param>
    /// <param name="pipeOptions">Optional pipe options to use.</param>
    /// <param name="cancellationToken">A cancellation token that aborts writing to the <paramref name="webSocket"/>.</param>
    /// <returns>A <see cref="PipeWriter"/>.</returns>
    public static PipeWriter UsePipeWriter(this WebSocket webSocket, PipeOptions? pipeOptions = null, CancellationToken cancellationToken = default)
    {
        var pipe = new Pipe(pipeOptions ?? PipeOptions.Default);
        Task.Run(async delegate
        {
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReadResult readResult = await pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    if (readResult.Buffer.Length > 0)
                    {
                        foreach (ReadOnlyMemory<byte> segment in readResult.Buffer)
                        {
                            await webSocket.SendAsync(segment, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    pipe.Reader.AdvanceTo(readResult.Buffer.End);
                    readResult.ScrubAfterAdvanceTo();

                    if (readResult.IsCompleted)
                    {
                        break;
                    }
                }

                await pipe.Reader.CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Propagate the exception to the writer.
                await pipe.Reader.CompleteAsync(ex).ConfigureAwait(false);
                return;
            }
        }).Forget();
        return pipe.Writer;
    }

    /// <summary>
    /// Removes the memory from <see cref="ReadResult"/> that may have been recycled by a call to <see cref="PipeReader.AdvanceTo(SequencePosition)"/>.
    /// </summary>
    /// <param name="readResult">The <see cref="ReadResult"/> to scrub.</param>
    /// <remarks>
    /// The <see cref="ReadResult.IsCanceled"/> and <see cref="ReadResult.IsCompleted"/> values are preserved, but the <see cref="ReadResult.Buffer"/> is made empty by this call.
    /// </remarks>
    private static void ScrubAfterAdvanceTo(this ref ReadResult readResult) => readResult = new ReadResult(default, readResult.IsCanceled, readResult.IsCompleted);

    // END: copy-pasted from Nerdbank.Streams@3ec3e25ba48f3d59fe8c1220563d8fe71a17b8d8

    private static void Forget(this Task t)
    {
        // the point of this is probably just to avoid unobserved exceptions?
        _ = ForgetInner(t);
        static async Task ForgetInner(Task t) => await t.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    public readonly struct GetOffSyncContextAwaitableAndAwaiter : INotifyCompletion
    {
        public GetOffSyncContextAwaitableAndAwaiter GetAwaiter() => default;

        public bool IsCompleted => SynchronizationContext.Current is null;

        public void GetResult() { }

        public void OnCompleted(Action continuation)
        {
            SynchronizationContext? suppressed = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                continuation();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(suppressed);
            }
        }
    }
}
