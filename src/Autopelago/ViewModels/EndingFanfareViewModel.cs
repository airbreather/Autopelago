using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia.ReactiveUI;

using ReactiveUI;

namespace Autopelago.ViewModels;

public sealed class EndingFanfareViewModel : ViewModelBase, IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public EndingFanfareViewModel()
    {
        _disposables.Add(Frames);
        _disposables.Add(Observable.Interval(TimeSpan.FromMilliseconds(500), AvaloniaScheduler.Instance)
            .Subscribe(_ => Frames.NextFrame())
        );
    }

    public BitmapPair Frames { get; } =
        LandmarkRegionViewModel.ReadFrames(GameDefinitions.Instance.GoalRegion.Key).Saturated;

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public required ReactiveCommand<Unit, Unit> BackToMapCommand { get; init; }
}
