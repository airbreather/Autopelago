using System.Reactive.Disposables;
using System.Reactive.Subjects;

using ReactiveUI.SourceGenerators;

namespace Autopelago.ViewModels;

public enum ConfirmItemHintResult
{
    Cancel,
    Ok,
}

public sealed partial class ConfirmItemHintViewModel : ViewModelBase, IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    [Reactive] private CollectableItemViewModel? _item;

    public ConfirmItemHintViewModel()
    {
        _disposables.Add(Result);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public AsyncSubject<ConfirmItemHintResult> Result { get; } = new();

    [ReactiveCommand]
    private void SetResult(ConfirmItemHintResult result)
    {
        Result.OnNext(result);
        Result.OnCompleted();
    }
}
