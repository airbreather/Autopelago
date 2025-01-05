using Avalonia;

using ReactiveUI.SourceGenerators;

namespace Autopelago.ViewModels;

public sealed partial class FillerLocationViewModel : ViewModelBase
{
    [Reactive] private bool _checked;

    [Reactive] private bool _relevant = true;

    public FillerLocationViewModel(LocationKey location, Point point)
    {
        Model = GameDefinitions.Instance[location];
        Point = point;
        PointWhenRenderingDot = point + FillerRegionViewModel.ToCenter;
    }

    public LocationDefinitionModel Model { get; }

    public Point Point { get; }

    public Point PointWhenRenderingDot { get; }
}
