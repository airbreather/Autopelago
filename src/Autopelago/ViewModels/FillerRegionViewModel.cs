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
        ["after_pirate_bake_sale"] = [new(168, 34), new(183, 34),
            new(185.44, 34.12), new(189.16, 34.35), new(192.08, 34.84), new(194.46, 35.47), new(197.73, 36.71), new(200.45, 38.11), new(204.34, 40.8), new(206.22, 42.44), new(208.74, 45.06), new(210.48, 47.19), new(212.72, 50.39), new(213.99, 52.49), new(215.71, 55.73), new(217.51, 59.72), new(219.29, 64.54), new(220.79, 69.64), new(221.69, 73.51), new(222.2, 76.42),
            new(223, 77), new(252, 77)],
        ["after_restaurant"] = [new(168, 120), new(219, 120), new(219, 105), new(185, 105), new(185, 91), new(238, 91), new(238, 77), new(252, 77)],
        ["before_captured_goldfish"] = [new(256, 77), new(290, 77), new(290, 104)],
        ["before_computer_interface"] = [new(290, 108), new(290, 225), new(284, 225)],
        ["before_kart_races"] = [new(281, 108), new(237, 179)],
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
