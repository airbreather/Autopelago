using System.Reactive;
using System.Reactive.Linq;

using Avalonia.Media.Imaging;
using Avalonia.ReactiveUI;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Autopelago.ViewModels;

public sealed class EndingFanfareViewModel : ViewModelBase, IDisposable
{
    private readonly Bitmap[] _frames = LandmarkRegionViewModel.ReadFrames(GameDefinitions.Instance.GoalRegion.Key, false).Saturated;

    private readonly IDisposable _frameInterval;

    private long _frameNum;

    public EndingFanfareViewModel()
    {
        _frameInterval = Observable.Interval(TimeSpan.FromMilliseconds(500), AvaloniaScheduler.Instance)
            .Subscribe(_ => Image = _frames[(_frameNum++) & 1]);
    }

    public void Dispose()
    {
        _frameInterval.Dispose();
        foreach (Bitmap frame in _frames)
        {
            frame.Dispose();
        }
    }

    [Reactive]
    public Bitmap? Image { get; private set; }

    public required ReactiveCommand<Unit, Unit> BackToMapCommand { get; init; }
}
