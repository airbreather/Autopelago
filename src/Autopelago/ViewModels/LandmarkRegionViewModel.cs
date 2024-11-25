using System.Collections.Frozen;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using SkiaSharp;

namespace Autopelago.ViewModels;

public sealed class LandmarkRegionViewModel : ViewModelBase, IDisposable
{
    private static readonly Vector s_toCenter = new Vector(16, 16) / 2;

    private static readonly Lazy<(Bitmap[] Yellow, Bitmap[] Gray)> s_questFrames = new(() => (ReadFrames("yellow_quest", false).Saturated, ReadFrames("gray_quest", false).Saturated));

    private static readonly FrozenDictionary<string, Point> s_canvasLocations = new[]
    {
        KeyValuePair.Create("basketball", new Point(59, 77)),
        KeyValuePair.Create("prawn_stars", new Point(103, 34)),
        KeyValuePair.Create("angry_turtles", new Point(103, 120)),
        KeyValuePair.Create("pirate_bake_sale", new Point(166, 34)),
        KeyValuePair.Create("restaurant", new Point(166, 120)),
        KeyValuePair.Create("bowling_ball_door", new Point(254, 77)),
        KeyValuePair.Create("captured_goldfish", new Point(290, 106)),
        KeyValuePair.Create("computer_interface", new Point(282, 225)),
        KeyValuePair.Create("kart_races", new Point(235, 179)),
        KeyValuePair.Create("trapeze", new Point(235, 225)),
        KeyValuePair.Create("daring_adventurer", new Point(235, 269)),
        KeyValuePair.Create("broken_down_bus", new Point(178, 179)),
        KeyValuePair.Create("blue_colored_screen_interface", new Point(178, 225)),
        KeyValuePair.Create("overweight_boulder", new Point(178, 269)),
        KeyValuePair.Create("binary_tree", new Point(124, 179)),
        KeyValuePair.Create("copyright_mouse", new Point(124, 225)),
        KeyValuePair.Create("computer_ram", new Point(124, 269)),
        KeyValuePair.Create("rat_rap_battle", new Point(67, 179)),
        KeyValuePair.Create("room_full_of_typewriters", new Point(67, 225)),
        KeyValuePair.Create("stack_of_crates", new Point(67, 269)),
        KeyValuePair.Create("secret_cache", new Point(20, 225)),
        KeyValuePair.Create("makeshift_rocket_ship", new Point(25, 331)),
        KeyValuePair.Create("roboclop_the_robot_war_horse", new Point(73, 353)),
        KeyValuePair.Create("homeless_mummy", new Point(84, 402)),
        KeyValuePair.Create("frozen_assets", new Point(54, 435)),
        KeyValuePair.Create("alien_vending_machine", new Point(114, 428)),
        KeyValuePair.Create("stalled_rocket_get_out_and_push", new Point(113, 334)),
        KeyValuePair.Create("seal_of_fortune", new Point(149, 381)),
        KeyValuePair.Create("space_opera", new Point(183, 346)),
        KeyValuePair.Create("minotaur_labyrinth", new Point(194, 399)),
        KeyValuePair.Create("asteroid_with_pants", new Point(232, 406)),
        KeyValuePair.Create("snakes_on_a_planet", new Point(243, 354)),
        KeyValuePair.Create("moon_comma_the", new Point(284, 319)),
    }.ToFrozenDictionary();

    private readonly IDisposable _clearAvailableSubscription;

    private readonly IDisposable _updateImagesSubscription;

    private readonly IDisposable _watchToolTipsSubscription;

    private readonly Bitmap[] _saturated;

    private readonly Bitmap[] _desaturated;

    public LandmarkRegionViewModel(string regionKey)
    {
        RegionKey = regionKey;
        Region = GameDefinitions.Instance.LandmarkRegions[regionKey];
        Location = Region.Locations[0];
        GameRequirementToolTipSource = new(Region.Requirement);
        CanvasLocation = s_canvasLocations[regionKey] - s_toCenter;

        (_saturated, Bitmap[]? desaturated) = ReadFrames(regionKey, true);
        _desaturated = desaturated!;
        (Bitmap[] yellowQuestFrames, Bitmap[] grayQuestFrames) = s_questFrames.Value;

        _clearAvailableSubscription = this
            .WhenAnyValue(x => x.Checked)
            .Subscribe(isChecked => Available &= !isChecked);

        _updateImagesSubscription = this
            .WhenAnyValue(x => x.FrameCounter, x => x.Checked, x => x.Available, (frameCounter, isChecked, isAvailable) => (frameCounter, isChecked, isAvailable))
            .Subscribe(curr =>
            {
                Image = (curr.isChecked ? _saturated : _desaturated)[curr.frameCounter & 1];
                SaturatedImage = _saturated[curr.frameCounter & 1];
                QuestImage = curr.isChecked ? null : (curr.isAvailable ? yellowQuestFrames : grayQuestFrames)[curr.frameCounter & 1];
            });

        _watchToolTipsSubscription = GameRequirementToolTipSource
            .WhenAnyValue(x => x.Satisfied)
            .Subscribe(satisfied => Available = satisfied && !Checked);
    }

    public void Dispose()
    {
        _clearAvailableSubscription.Dispose();
        _updateImagesSubscription.Dispose();
        _watchToolTipsSubscription.Dispose();
        foreach (Bitmap saturated in _saturated)
        {
            saturated.Dispose();
        }

        foreach (Bitmap desaturated in _desaturated)
        {
            desaturated.Dispose();
        }
    }

    public string RegionKey { get; }

    public LandmarkRegionDefinitionModel Region { get; }

    public LocationDefinitionModel Location { get; }

    public GameRequirementToolTipViewModel GameRequirementToolTipSource { get; }

    public Point CanvasLocation { get; }

    [Reactive]
    public Bitmap? Image { get; private set; }

    [Reactive]
    public Bitmap? SaturatedImage { get; private set; }

    [Reactive]
    public Bitmap? QuestImage { get; private set; }

    [Reactive]
    public bool Available { get; set; }

    [Reactive]
    public bool Checked { get; set; }

    [Reactive]
    internal long FrameCounter { get; private set; }

    internal void NextFrame()
    {
        ++FrameCounter;
    }

    internal static (Bitmap[] Saturated, Bitmap[]? Desaturated) ReadFrames(string regionKey, bool andDesaturated)
    {
        using Stream data = AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{regionKey}.webp"));
        using SKCodec codec = SKCodec.Create(data);
        SKImageInfo imageInfo = codec.Info;
        SKCodecFrameInfo[] frameInfo = codec.FrameInfo;
        Bitmap[] saturated = new Bitmap[2];
        Bitmap[]? desaturated = andDesaturated ? new Bitmap[2] : null;
        if (frameInfo.Length is not (0 or 2))
        {
            throw new NotSupportedException("These were all supposed to be 1- or 2-frame images.");
        }

        for (int i = 0; i < frameInfo.Length; i++)
        {
            if (frameInfo[i].Duration != 500)
            {
                throw new NotSupportedException("These were all supposed to be 500ms.");
            }

            using SKBitmap bmp = new(imageInfo);
            codec.GetPixels(imageInfo, bmp.GetPixels(), new(i));
            bmp.SetImmutable();
            using SKImage img = SKImage.FromBitmap(bmp);
            using MemoryStream ms = new();
            using SKData encoded = img.Encode();
            encoded.SaveTo(ms);
            ms.Position = 0;
            saturated[i] = new(ms);
            if (desaturated is not null)
            {
                desaturated[i] = ToDesaturated(bmp);
            }
        }

        if (frameInfo.Length == 0)
        {
            using SKBitmap bmp = new(imageInfo);
            codec.GetPixels(imageInfo, bmp.GetPixels());
            bmp.SetImmutable();
            using SKImage img = SKImage.FromBitmap(bmp);
            using MemoryStream ms = new();
            using SKData encoded = img.Encode();
            encoded.SaveTo(ms);
            ms.Position = 0;
            saturated[0] = saturated[1] = new(ms);
            if (desaturated is not null)
            {
                desaturated[0] = desaturated[1] = ToDesaturated(bmp);
            }
        }

        return (saturated, desaturated);
    }
}
