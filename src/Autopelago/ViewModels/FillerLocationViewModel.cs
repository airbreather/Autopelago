using Avalonia;

using ReactiveUI.Fody.Helpers;

namespace Autopelago.ViewModels;

public sealed class FillerLocationViewModel : ViewModelBase
{
    public FillerLocationViewModel(LocationDefinitionModel model, Point point)
    {
        Model = model;
        Point = point;
        PointWhenRenderingDot = point + FillerRegionViewModel.ToCenter;
    }

    public LocationDefinitionModel Model { get; }

    public Point Point { get; }

    public Point PointWhenRenderingDot { get; }

    [Reactive]
    public bool Checked { get; set; }
}
