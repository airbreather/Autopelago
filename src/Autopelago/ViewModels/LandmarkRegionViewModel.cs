using System.Collections.Frozen;
using System.Reactive.Disposables;

using Avalonia;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Autopelago.ViewModels;

public sealed partial class LandmarkRegionViewModel : ViewModelBase, IDisposable
{
    private static readonly BitmapPair s_yellowQuestImages = new("yellow_quest");

    private static readonly BitmapPair s_grayQuestImages = new("gray_quest");

    private static readonly Vector s_toCenter = new Vector(16, 16) / 2;

    public static readonly Point MoonLocation = new Point(284, 319) - s_toCenter;

    private static readonly FrozenDictionary<RegionKey, Point> s_canvasLocations = new Dictionary<string, Point>
    {
        ["basketball"] = new(59, 77),
        ["prawn_stars"] = new(103, 34),
        ["angry_turtles"] = new(103, 120),
        ["pirate_bake_sale"] = new(166, 34),
        ["restaurant"] = new(166, 120),
        ["bowling_ball_door"] = new(254, 77),
        ["captured_goldfish"] = new(290, 106),
        ["computer_interface"] = new(282, 225),
        ["kart_races"] = new(235, 179),
        ["trapeze"] = new(235, 225),
        ["daring_adventurer"] = new(235, 269),
        ["broken_down_bus"] = new(178, 179),
        ["blue_colored_screen_interface"] = new(178, 225),
        ["overweight_boulder"] = new(178, 269),
        ["binary_tree"] = new(124, 179),
        ["copyright_mouse"] = new(124, 225),
        ["computer_ram"] = new(124, 269),
        ["rat_rap_battle"] = new(67, 179),
        ["room_full_of_typewriters"] = new(67, 225),
        ["stack_of_crates"] = new(67, 269),
        ["secret_cache"] = new(20, 225),
        ["makeshift_rocket_ship"] = new(25, 331),
        ["roboclop_the_robot_war_horse"] = new(73, 353),
        ["homeless_mummy"] = new(84, 402),
        ["frozen_assets"] = new(54, 435),
        ["alien_vending_machine"] = new(114, 428),
        ["stalled_rocket_get_out_and_push"] = new(113, 334),
        ["seal_of_fortune"] = new(149, 381),
        ["space_opera"] = new(183, 346),
        ["minotaur_labyrinth"] = new(194, 399),
        ["asteroid_with_pants"] = new(232, 406),
        ["snakes_on_a_planet"] = new(243, 354),
    }.ToFrozenDictionary(kvp => GameDefinitions.Instance.RegionsByYamlKey[kvp.Key], kvp => kvp.Value);

    private readonly CompositeDisposable _disposables = [];

    [Reactive] private bool _checked;

    [Reactive(SetModifier = AccessModifier.Private)] private bool _showSaturatedImage;

    [Reactive(SetModifier = AccessModifier.Private)] private bool _showDesaturatedImage = true;

    [Reactive(SetModifier = AccessModifier.Private)] private bool _showYellowQuestImage;

    [Reactive(SetModifier = AccessModifier.Private)] private bool _showGrayQuestImage = true;

    public LandmarkRegionViewModel(RegionKey region, IObservable<bool> lactoseIntolerant)
    {
        Region = (LandmarkRegionDefinitionModel)GameDefinitions.Instance[region];
        Location = GameDefinitions.Instance[GameDefinitions.Instance[region].Locations[0]];
        GameRequirementToolTipSource = new(((LandmarkRegionDefinitionModel)GameDefinitions.Instance[region]).Requirement, lactoseIntolerant);
        CanvasLocation = s_canvasLocations[region] - s_toCenter;

        SaturatedImages = new(Region.YamlKey);
        SaturatedImages.DisposeWith(_disposables);
        DesaturatedImages = SaturatedImages.ToDesaturated();
        DesaturatedImages.DisposeWith(_disposables);

        IDisposable whenRequirementSatisfied;
        _disposables.Add(whenRequirementSatisfied = GameRequirementToolTipSource.ObservableForProperty(x => x.Satisfied)
            .Subscribe(satisfied =>
            {
                if (_checked)
                {
                    return;
                }

                ShowYellowQuestImage = satisfied.Value;
                ShowGrayQuestImage = !satisfied.Value;
            }));

        this.ObservableForProperty(x => x.Checked)
            .Subscribe(isChecked =>
            {
                if (!isChecked.Value)
                {
                    return;
                }

                whenRequirementSatisfied.Dispose();
                ShowSaturatedImage = true;
                ShowDesaturatedImage = false;
                ShowYellowQuestImage = false;
                ShowGrayQuestImage = false;
            })
            .DisposeWith(_disposables);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public LandmarkRegionDefinitionModel Region { get; }

    public LocationDefinitionModel Location { get; }

    public GameRequirementToolTipViewModel GameRequirementToolTipSource { get; }

    public Point CanvasLocation { get; }

    public BitmapPair SaturatedImages { get; }

    public BitmapPair DesaturatedImages { get; }

    public BitmapPair YellowQuestImages => s_yellowQuestImages;

    public BitmapPair GrayQuestImages => s_grayQuestImages;
}
