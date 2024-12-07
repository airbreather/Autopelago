using System.Collections.Frozen;
using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Platform;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using SkiaSharp;

namespace Autopelago.ViewModels;

public sealed partial class BitmapPair : ViewModelBase, IDisposable
{
    [Reactive] private SKBitmap _toDraw = null!;

    public void Dispose()
    {
        A.Dispose();
        if (!ReferenceEquals(A, B))
        {
            B.Dispose();
        }
    }

    public required SKBitmap A { get; init; }

    public required SKBitmap B { get; init; }

    public void NextFrame()
    {
        if (!ReferenceEquals(A, B) || ReferenceEquals(_toDraw, null))
        {
            ToDraw = ReferenceEquals(_toDraw, A) ? B : A;
        }
    }
}

public sealed partial class LandmarkRegionViewModel : ViewModelBase, IDisposable
{
    private static readonly Vector s_toCenter = new Vector(16, 16) / 2;

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

    private readonly CompositeDisposable _disposables = [];

    [Reactive] private bool _checked;

    [Reactive(SetModifier = AccessModifier.Private)] private bool _showSaturatedImage;

    [Reactive(SetModifier = AccessModifier.Private)] private bool _showDesaturatedImage = true;

    [Reactive(SetModifier = AccessModifier.Private)] private bool _showYellowQuestImage;

    [Reactive(SetModifier = AccessModifier.Private)] private bool _showGrayQuestImage = true;

    public LandmarkRegionViewModel(string regionKey, BitmapPair yellowQuestImages, BitmapPair grayQuestImages)
    {
        RegionKey = regionKey;
        Region = GameDefinitions.Instance.LandmarkRegions[regionKey];
        Location = Region.Locations[0];
        GameRequirementToolTipSource = new(Region.Requirement);
        CanvasLocation = s_canvasLocations[regionKey] - s_toCenter;

        YellowQuestImages = yellowQuestImages;
        GrayQuestImages = grayQuestImages;
        (SaturatedImages, DesaturatedImages) = ReadFrames(regionKey);
        _disposables.Add(SaturatedImages);
        _disposables.Add(DesaturatedImages);

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

        _disposables.Add(this.ObservableForProperty(x => x.Checked)
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
            }));
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public string RegionKey { get; }

    public LandmarkRegionDefinitionModel Region { get; }

    public LocationDefinitionModel Location { get; }

    public GameRequirementToolTipViewModel GameRequirementToolTipSource { get; }

    public Point CanvasLocation { get; }

    public BitmapPair SaturatedImages { get; }

    public BitmapPair DesaturatedImages { get; }

    public BitmapPair YellowQuestImages { get; }

    public BitmapPair GrayQuestImages { get; }

    public void NextFrame()
    {
        SaturatedImages.NextFrame();
        DesaturatedImages.NextFrame();
    }

    public static (BitmapPair Saturated, BitmapPair Desaturated) CreateQuestImages()
    {
        return (ReadFrames("yellow_quest").Saturated, ReadFrames("gray_quest").Saturated);
    }

    internal static (BitmapPair Saturated, BitmapPair Desaturated) ReadFrames(string regionKey)
    {
        using Stream data = AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{regionKey}.webp"));
        using SKCodec codec = SKCodec.Create(data);
        SKImageInfo imageInfo = codec.Info;
        SKCodecFrameInfo[] frameInfo = codec.FrameInfo;
        SKBitmap[] saturated = new SKBitmap[2];
        SKBitmap[] desaturated = new SKBitmap[2];
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

            SKBitmap bmp = new(imageInfo);
            codec.GetPixels(imageInfo, bmp.GetPixels(), new(i));
            bmp.SetImmutable();
            saturated[i] = bmp;
            desaturated[i] = ToDesaturated(bmp);
        }

        if (frameInfo.Length == 0)
        {
            SKBitmap bmp = new(imageInfo);
            codec.GetPixels(imageInfo, bmp.GetPixels());
            bmp.SetImmutable();
            saturated[0] = saturated[1] = bmp;
            desaturated[0] = desaturated[1] = ToDesaturated(bmp);
        }

        return (new() { A = saturated[0], B = saturated[1] }, new() { A = desaturated[0], B = desaturated[1] });
    }
}
