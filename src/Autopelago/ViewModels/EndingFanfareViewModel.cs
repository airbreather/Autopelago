using System.Reactive;
using System.Reactive.Disposables;

using ReactiveUI;

namespace Autopelago.ViewModels;

public sealed class EndingFanfareViewModel : ViewModelBase, IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public EndingFanfareViewModel()
    {
        MoonCommaThe.DisposeWith(_disposables);
    }

    public BitmapPair MoonCommaThe { get; } = new("moon_comma_the");

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public required ReactiveCommand<Unit, Unit> BackToMapCommand { get; init; }
}
