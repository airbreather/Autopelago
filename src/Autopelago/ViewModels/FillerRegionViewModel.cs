using System.Collections.Frozen;
using System.Collections.Immutable;

using Avalonia;

namespace Autopelago.ViewModels;

public sealed class FillerRegionViewModel : ViewModelBase
{
    private static readonly Vector s_toCenter = new Point(16, 16) / 2;

    private static readonly FrozenDictionary<string, ImmutableArray<Point>> s_definingPoints = new Dictionary<string, ImmutableArray<Point>>
    {
        ["Menu"] = [new(0, 77), new(57, 77)],
        ["before_prawn_stars"] = [new(61, 77), new(90, 77), new(90, 34), new(101, 34)],
        ["before_angry_turtles"] = [new(61, 77), new(90, 77), new(90, 120), new(101, 120)],
        ["before_pirate_bake_sale"] = [new(105, 34), new(164, 34)],
        ["before_restaurant"] = [new(105, 120), new(164, 120)],
        ["after_pirate_bake_sale"] = [new(168, 34), new(191, 35), new(195, 36), new(198, 37), new(202, 39), new(205, 41), new(209, 45), new(212, 49), new(215, 56), new(217, 67), new(217, 77), new(252, 77)],
        ["after_restaurant"] = [new(168, 120), new(219, 120), new(219, 105), new(185, 105), new(185, 91), new(238, 91), new(238, 77), new(252, 77)],
    }.ToFrozenDictionary(kvp => kvp.Key, kvp => ImmutableArray.CreateRange(kvp.Value, p => p - s_toCenter));

    public FillerRegionViewModel(FillerRegionDefinitionModel model)
    {
        // for clarity, in this method:
        // - "location" is the LocationDefinitionModel kind
        // - "point" WITHOUT "prj" is the (x, y) kind
        // - "point" WITH "prj" is the projection of an (x, y) point onto a line
        // - "endpoint" is the endpoint of a segment that ends at the indicated point. the "prj"
        //   convention from "point" applies here, too.
        Model = model;
        ImmutableArray<Point> definingPoints = s_definingPoints[model.Key];
        Span<double> endpointsPrj = definingPoints.Length > 100
            ? new double[definingPoints.Length]
            : stackalloc double[definingPoints.Length];

        endpointsPrj[0] = 0;
        for (int i = 1; i < endpointsPrj.Length; i++)
        {
            endpointsPrj[i] = endpointsPrj[i - 1] + Point.Distance(definingPoints[i - 1], definingPoints[i]);
        }

        ImmutableArray<Point>.Builder locationPointsBuilder = ImmutableArray.CreateBuilder<Point>(Model.Locations.Length);
        locationPointsBuilder.Count = Model.Locations.Length;
        locationPointsBuilder[0] = definingPoints[0];
        int endpointIndex = 1;
        for (int i = 1; i < Model.Locations.Length; i++)
        {
            double nextPointPrjOnPath = endpointsPrj[^1] * (i / (double)(Model.Locations.Length - 1));
            while (nextPointPrjOnPath > endpointsPrj[endpointIndex])
            {
                endpointIndex++;
            }

            double nextPointPrjOnSegment = nextPointPrjOnPath - endpointsPrj[endpointIndex - 1];
            double p1Share = nextPointPrjOnSegment / (endpointsPrj[endpointIndex] - endpointsPrj[endpointIndex - 1]);
            double p0Share = 1 - p1Share;
            Point p0 = definingPoints[endpointIndex - 1];
            Point p1 = definingPoints[endpointIndex];
            locationPointsBuilder[i] = (p0 * p0Share) + (p1 * p1Share);
        }

        LocationPoints = locationPointsBuilder.MoveToImmutable();
    }

    public FillerRegionDefinitionModel Model { get; }

    public ImmutableArray<Point> LocationPoints { get; }
}
