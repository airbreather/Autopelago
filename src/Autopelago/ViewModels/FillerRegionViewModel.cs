using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;

using Avalonia;

namespace Autopelago.ViewModels;

public sealed class FillerRegionViewModel : ViewModelBase
{
    internal static readonly Vector ToCenter = new Point(16, 16) / 2;

#pragma warning disable format
    private static readonly FrozenDictionary<string, ImmutableArray<Point>> s_definingPoints = new Dictionary<string, Point[]>
    {
        ["Menu"] = [new(0, 77), new(57, 77)],
        ["before_prawn_stars"] = [new(61, 74), new(90, 74), new(90, 34), new(101, 34)],
        ["before_angry_turtles"] = [new(61, 80), new(90, 80), new(90, 120), new(101, 120)],
        ["before_pirate_bake_sale"] = [new(105, 34), new(164, 34)],
        ["before_restaurant"] = [new(105, 120), new(164, 120)],
        ["after_pirate_bake_sale"] = [new(168, 34), new(183, 34),
            new(185.44, 34.12), new(189.16, 34.35), new(192.08, 34.84), new(194.46, 35.47), new(197.73, 36.71), new(200.45, 38.11), new(204.34, 40.8), new(206.22, 42.44), new(208.74, 45.06), new(210.48, 47.19), new(212.72, 50.39), new(213.99, 52.49), new(215.71, 55.73), new(217.51, 59.72), new(219.29, 64.54), new(220.79, 69.64), new(221.69, 71.51), new(222.2, 73.42),
            new(223, 75), new(252, 75)],
        ["after_restaurant"] = [new(168, 120), new(219, 120), new(219, 105), new(185, 105), new(185, 91), new(238, 91), new(241, 80), new(252, 80)],
        ["before_captured_goldfish"] = [new(256, 77), new(290, 77), new(290, 104)],
        ["before_computer_interface"] = [new(290, 108), new(290, 225), new(284, 225)],
        ["before_kart_races"] = [new(281, 223), new(237, 179)],
        ["before_daring_adventurer"] = [new(280, 227), new(237, 269)],
        ["before_broken_down_bus"] = [new(233, 179), new(180, 179)],
        ["before_overweight_boulder"] = [new(233, 269), new(180, 269)],
        ["before_copyright_mouse"] = [new(176, 179), new(167, 179), new(124, 223)],
        ["before_blue_colored_screen_interface"] = [new(178, 267), new(178, 227)],
        ["before_room_full_of_typewriters"] = [new(122, 225), new(69, 225)],
        ["before_trapeze"] = [new(180, 225), new(233, 225)],
        ["before_binary_tree"] = [new(124, 223), new(124, 181)],
        ["before_computer_ram"] = [new(177, 227), new(135, 269), new(126, 269)],
        ["before_rat_rap_battle"] = [new(122, 179), new(69, 179)],
        ["before_stack_of_crates"] = [new(122, 269), new(69, 269)],
        ["after_rat_rap_battle"] = [new(65, 179), new(21, 223)],
        ["after_stack_of_crates"] = [new(65, 269), new(62, 269), new(20, 227)],
        ["before_makeshift_rocket_ship"] = [new(18, 225), new(6, 225), new(6, 301),
            new(6.6, 301.89), new(6.6, 309.36), new(7.25, 312.51), new(7.88, 321.07), new(8.1, 329.35), new(8.78, 333.37), new(10.39, 339.25), new(12.12, 341.94), new(15.63, 345.22), new(19.91, 347.19), new(28.01, 348.26), new(34.65, 348.53), new(39.42, 344.52), new(41.18, 339.09), new(41.39, 332.27), new(39.81, 327.85), new(35.69, 325.24), new(32.55, 325.14), new(28.95, 328.54),
            new(26, 330)],
        ["before_roboclop_the_robot_war_horse"] = [new(26, 330),
            new(26.97, 331.42), new(30.98, 331.87), new(32.51, 332.98), new(34.2, 336.21), new(34.12, 340.35), new(32.25, 343.41), new(29.73, 344.24), new(26.32, 344.07), new(21.22, 342.68), new(18.46, 341.34), new(15.85, 338.21), new(14.47, 335.2), new(13.34, 329.66), new(15.26, 324.43), new(16.99, 321.45), new(20.73, 317.9), new(28.34, 315.93), new(36.19, 316.15), new(43.81, 318.42), new(50.44, 324.81), new(50.74, 326.38), new(55.42, 335.44), new(56.84, 338.87), new(59.69, 344.3), new(63.05, 347.94), new(69.93, 351.34),
            new(72, 353)],
        ["before_stalled_rocket_get_out_and_push"] = [new(74, 352), new(112, 335)],
        ["before_homeless_mummy"] = [new(73, 354), new(84, 401)],
        ["after_stalled_rocket_get_out_and_push"] = [new(114, 333), new(148, 380)],
        ["before_frozen_assets"] = [new(83, 403), new(55, 434)],
        ["before_alien_vending_machine"] = [new(85, 403), new(113, 427)],
        ["after_homeless_mummy"] = [new(85, 402), new(148, 382)],
        ["before_space_opera"] = [new(149, 380), new(156, 371), new(157, 371), new(174, 354), new(175, 354), new(182, 347)],
        ["before_minotaur_labyrinth"] = [new(150, 382), new(193, 399)],
        ["after_space_opera"] = [new(184, 346), new(242, 354)],
        ["before_asteroid_with_pants"] = [new(195, 400), new(231, 406)],
        ["after_minotaur_labyrinth"] = [new(195, 398), new(207, 386), new(209, 385), new(216, 378), new(218, 377), new(226, 369), new(228, 368), new(236, 360), new(238, 359), new(242, 355)],
    }.ToFrozenDictionary(kvp => kvp.Key, kvp => ConvertDefiningPoints(kvp.Key, kvp.Value));
#pragma warning restore format

    public FillerRegionViewModel(FillerRegionDefinitionModel model)
    {
        // for clarity, in this method:
        // - "location" is the LocationDefinitionModel kind
        // - "point" WITHOUT "prj" is the (x, y) kind
        // - "point" WITH "prj" is the projection of an (x, y) point onto a line
        // - "endpoint" is the endpoint of a segment that ends at the indicated point. the "prj"
        //   convention from "point" applies here, too.
        Model = model;
        ReadOnlySpan<Point> definingPoints = s_definingPoints[model.Key].AsSpan();
        ReadOnlySpan<double> endpointsPrj = IndexLine(
            definingPoints,
            definingPoints.Length > 100
                ? new double[definingPoints.Length]
                : stackalloc double[definingPoints.Length]);

        ImmutableArray<FillerLocationViewModel>.Builder locationsBuilder = ImmutableArray.CreateBuilder<FillerLocationViewModel>(Model.Locations.Length);
        locationsBuilder.Count = Model.Locations.Length;
        for (int i = 0; i < Model.Locations.Length; i++)
        {
            double prj = (i / ((double)model.Locations.Length - 1)) * endpointsPrj[^1];
            Point projected = Project(prj, definingPoints, endpointsPrj);
            locationsBuilder[i] = new(Model.Locations[i], projected);
        }

        Locations = locationsBuilder.MoveToImmutable();
    }

    public FillerRegionDefinitionModel Model { get; }

    public ImmutableArray<FillerLocationViewModel> Locations { get; }

    private static ImmutableArray<Point> ConvertDefiningPoints(string regionKey, ReadOnlySpan<Point> definingPointsOrig)
    {
        // the starting and ending points are RIGHT on top of their corresponding landmark locations
        // (by design, since that's how I figured them out). this makes the filler dots hard to read
        // on the map, yes, but it also makes movement through the landmark locations look sluggish:
        // each landmark location has two filler locations RIGHT next to it, and the player moves at
        // a constant speed in terms of LOCATIONS (not in terms of SCREEN COORDINATES), so unless we
        // pad them out like this, those look like little speed bumps. perhaps it would have been OK
        // in isolation, but now it would cause the dots to draw on top of the icons of the landmark
        // locations, which is a no-go. so instead, we have this helper to take what would have been
        // drawn as a line from landmark to landmark, subtract a count of pixels from each endpoint,
        // move the middle points in proportion to where they would have been on the longer line but
        // for this, and then move the line that count of pixels in towards the middle.
        const int densifyMultiplier = 20;
        int densifiedLength = ((definingPointsOrig.Length - 1) * densifyMultiplier) + 1;
        using IMemoryOwner<Point> pointsOwner = MemoryPool<Point>.Shared.Rent(densifiedLength * 2);
        using IMemoryOwner<double> prjOwner = MemoryPool<double>.Shared.Rent(densifiedLength * 2);
        Span<Point> densifiedDefiningPoints = pointsOwner.Memory[..densifiedLength].Span;
        Span<Point> densifiedTargetPoints = pointsOwner.Memory[(^densifiedLength)..].Span;
        Span<double> densifiedDefiningPointsPrj = prjOwner.Memory[..densifiedLength].Span;
        Span<double> densifiedTargetPointsPrj = prjOwner.Memory[(^densifiedLength)..].Span;

        Densify(definingPointsOrig, densifiedDefiningPoints);

        // rename things so we don't need to have the word "densified" at all after this block.
        Span<Point> definingPoints = densifiedDefiningPoints;
        Span<Point> targetPoints = densifiedTargetPoints;
        Span<double> definingPointsPrj = densifiedDefiningPointsPrj;
        Span<double> targetPointsPrj = densifiedTargetPointsPrj;

        IndexLine(definingPoints, definingPointsPrj);
        definingPointsPrj.CopyTo(targetPointsPrj);

        double originalLength = definingPointsPrj[^1];

        const double paddingAtBeginning = 8;
        const double paddingAtEnd = 8;
        if (regionKey == GameDefinitions.Instance.StartRegion.Key)
        {
            // start region needs less padding at the start
            const double startRegionPaddingAtBeginning = 2;
            double newProportion = (originalLength - paddingAtEnd - startRegionPaddingAtBeginning) / originalLength;
            foreach (ref double targetPointPrj in targetPointsPrj)
            {
                targetPointPrj = (targetPointPrj * newProportion) + startRegionPaddingAtBeginning;
            }
        }
        else
        {
            // all other filler regions need padding on both sides.
            double newProportion = (originalLength - paddingAtBeginning - paddingAtEnd) / originalLength;
            foreach (ref double targetPointPrj in targetPointsPrj)
            {
                targetPointPrj = (targetPointPrj * newProportion) + paddingAtBeginning;
            }
        }

        for (int i = 0; i < definingPoints.Length; i++)
        {
            targetPoints[i] = Project(targetPointsPrj[i], definingPoints, definingPointsPrj) - ToCenter;
        }

        return [.. targetPoints];
    }

    private static void Densify(ReadOnlySpan<Point> definingPoints, Span<Point> densifiedPoints)
    {
        int densifyMultiplier = (densifiedPoints.Length - 1) / (definingPoints.Length - 1);
        Debug.Assert(((definingPoints.Length - 1) * densifyMultiplier) + 1 == densifiedPoints.Length);
        densifiedPoints[0] = definingPoints[0];
        for (int i = 1; i < definingPoints.Length; i++)
        {
            Point p0 = definingPoints[i - 1];
            Point p1 = definingPoints[i];
            for (int j = 0; j < densifyMultiplier; j++)
            {
                double p1Share = (densifyMultiplier - j) / (double)densifyMultiplier;
                densifiedPoints[(i * densifyMultiplier) - j] =
                    (p0 * (1 - p1Share)) +
                    (p1 * p1Share);
            }
        }
    }

    private static Span<double> IndexLine(ReadOnlySpan<Point> definingPoints, Span<double> endpointsPrj)
    {
        endpointsPrj[0] = 0;
        for (int i = 1; i < endpointsPrj.Length; i++)
        {
            endpointsPrj[i] = endpointsPrj[i - 1] + Point.Distance(definingPoints[i - 1], definingPoints[i]);
        }

        return endpointsPrj;
    }

    private static Point Project(double prj, ReadOnlySpan<Point> definingPoints, ReadOnlySpan<double> definingPointsPrj)
    {
        // ASSUMPTION: prj <= definingPointsPrj[^1]
        for (int i = 0; i < definingPointsPrj.Length - 1; i++)
        {
            if (definingPointsPrj[i + 1] < prj)
            {
                continue;
            }

            double segPos = prj - definingPointsPrj[i];
            double segLen = definingPointsPrj[i + 1] - definingPointsPrj[i];
            double p1Share = segPos / segLen;
            return
                (definingPoints[i] * (1 - p1Share)) +
                (definingPoints[i + 1] * p1Share);
        }

        throw new ArgumentException("must be between the actual projected endpoints");
    }
}
