using System.Runtime.ExceptionServices;

namespace ArchipelagoClientDotNet;

// optimized to allocate as little as reasonably possible, especially when there are few elements:
// - for 0 or 1 elements, this type allocates nothing, ever.
// - for 2+ elements whose own Dispose() methods never throw, this type wraps a List<T> and
//   allocates nothing else, ever.
// - for 2+ elements where only one throws from its own Dispose() method, then our own Dispose()
//   method will only allocate what's needed for the ExceptionDispatchInfo to rethrow at the end.
// - for 2+ elements where multiple of them throw from their own Dispose() methods, it's basically
//   equivalent to what a naive version would look like.
// the main drawback, of course, is that it's a mutable value type with reference semantics.
internal struct AFewDisposables : IDisposable
{
    private object? _disposables;

    public void Add(IDisposable disposable)
    {
        List<IDisposable> list;
        switch (_disposables)
        {
            case null:
                _disposables = disposable;
                return;

            case IDisposable singleOther:
                 _disposables = list = new List<IDisposable>(2) { singleOther };
                break;

            case { } lst:
                list = (List<IDisposable>)lst;
                break;
        }

        list.Add(disposable);
    }

    public readonly void Dispose()
    {
        List<IDisposable> list;
        switch (_disposables)
        {
            case null:
                return;

            case IDisposable single:
                single.Dispose();
                return;

            case { } lst:
                list = (List<IDisposable>)lst;
                break;
        }

        ExceptionDispatchInfo? firstException = null;
        List<Exception>? exceptions = null;
        foreach (IDisposable disposable in list)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                if (exceptions is null)
                {
                    if (firstException is { } prev)
                    {
                        exceptions = [prev.SourceException, ex];
                        firstException = null;
                    }
                    else
                    {
                        firstException = ExceptionDispatchInfo.Capture(ex);
                    }
                }
                else
                {
                    exceptions.Add(ex);
                }
            }
        }

        firstException?.Throw();
        exceptions?.Throw();
    }
}
