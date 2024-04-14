using System.Collections.Frozen;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using SkiaSharp;

namespace Autopelago.ViewModels;

public sealed class CheckableLocationViewModel : ViewModelBase, IDisposable
{
    private const int HalfWidth = 8;

    private static readonly Lazy<(Bitmap[] Yellow, Bitmap[] Gray)> s_questFrames = new(() => (ReadFrames("yellow_quest").Saturated, ReadFrames("gray_quest").Saturated));

    private static readonly FrozenDictionary<string, Point> s_canvasLocations = new[]
    {
        KeyValuePair.Create("basketball", new Point(59 - HalfWidth, 77 - HalfWidth)),
        KeyValuePair.Create("prawn_stars", new Point(103 - HalfWidth, 34 - HalfWidth)),
        KeyValuePair.Create("angry_turtles", new Point(103 - HalfWidth, 120 - HalfWidth)),
        KeyValuePair.Create("pirate_bake_sale", new Point(166 - HalfWidth, 34 - HalfWidth)),
        KeyValuePair.Create("restaurant", new Point(166 - HalfWidth, 120 - HalfWidth)),
        KeyValuePair.Create("bowling_ball_door", new Point(254 - HalfWidth, 77 - HalfWidth)),
        KeyValuePair.Create("captured_goldfish", new Point(290 - HalfWidth, 106 - HalfWidth)),
        KeyValuePair.Create("computer_interface", new Point(282 - HalfWidth, 225 - HalfWidth)),
        KeyValuePair.Create("kart_races", new Point(235 - HalfWidth, 179 - HalfWidth)),
        KeyValuePair.Create("trapeze", new Point(235 - HalfWidth, 225 - HalfWidth)),
        KeyValuePair.Create("daring_adventurer", new Point(235 - HalfWidth, 269 - HalfWidth)),
        KeyValuePair.Create("broken_down_bus", new Point(178 - HalfWidth, 179 - HalfWidth)),
        KeyValuePair.Create("blue_colored_screen_interface", new Point(178 - HalfWidth, 225 - HalfWidth)),
        KeyValuePair.Create("overweight_boulder", new Point(178 - HalfWidth, 269 - HalfWidth)),
        KeyValuePair.Create("binary_tree", new Point(124 - HalfWidth, 179 - HalfWidth)),
        KeyValuePair.Create("copyright_mouse", new Point(124 - HalfWidth, 225 - HalfWidth)),
        KeyValuePair.Create("computer_ram", new Point(124 - HalfWidth, 269 - HalfWidth)),
        KeyValuePair.Create("rat_rap_battle", new Point(67 - HalfWidth, 179 - HalfWidth)),
        KeyValuePair.Create("room_full_of_typewriters", new Point(67 - HalfWidth, 225 - HalfWidth)),
        KeyValuePair.Create("stack_of_crates", new Point(67 - HalfWidth, 269 - HalfWidth)),
        KeyValuePair.Create("secret_cache", new Point(20 - HalfWidth, 225 - HalfWidth)),
        KeyValuePair.Create("makeshift_rocket_ship", new Point(25 - HalfWidth, 331 - HalfWidth)),
        KeyValuePair.Create("roboclop_the_robot_war_horse", new Point(73 - HalfWidth, 353 - HalfWidth)),
        KeyValuePair.Create("homeless_mummy", new Point(84 - HalfWidth, 402 - HalfWidth)),
        KeyValuePair.Create("frozen_assets", new Point(54 - HalfWidth, 435 - HalfWidth)),
        KeyValuePair.Create("alien_vending_machine", new Point(114 - HalfWidth, 428 - HalfWidth)),
        KeyValuePair.Create("stalled_rocket_get_out_and_push", new Point(113 - HalfWidth, 334 - HalfWidth)),
        KeyValuePair.Create("seal_of_fortune", new Point(149 - HalfWidth, 381 - HalfWidth)),
        KeyValuePair.Create("space_opera", new Point(183 - HalfWidth, 346 - HalfWidth)),
        KeyValuePair.Create("minotaur_labyrinth", new Point(194 - HalfWidth, 399 - HalfWidth)),
        KeyValuePair.Create("asteroid_with_pants", new Point(232 - HalfWidth, 406 - HalfWidth)),
        KeyValuePair.Create("snakes_on_a_planet", new Point(243 - HalfWidth, 354 - HalfWidth)),
        KeyValuePair.Create("moon_comma_the", new Point(284 - HalfWidth, 319 - HalfWidth)),
    }.ToFrozenDictionary();

    private readonly IDisposable _clearAvailableSubscription;

    private readonly IDisposable _updateImagesSubscription;

    private readonly Bitmap[] _saturated;

    private readonly Bitmap[] _desaturated;

    public CheckableLocationViewModel(string locationKey)
    {
        LocationKey = locationKey;
        Model = GameDefinitions.Instance.LandmarkRegions[locationKey].Locations[0];
        CanvasLocation = s_canvasLocations[locationKey];

        (_saturated, _desaturated) = ReadFrames(locationKey);
        (Bitmap[] yellowQuestFrames, Bitmap[] grayQuestFrames) = s_questFrames.Value;

        _clearAvailableSubscription = this
            .WhenAnyValue(x => x.Checked)
            .Subscribe(isChecked => Available &= !isChecked);

        _updateImagesSubscription = this
            .WhenAnyValue(x => x.FrameCounter, x => x.Checked, x => x.Available, (frameCounter, isChecked, isAvailable) => (frameCounter, isChecked, isAvailable))
            .Subscribe(curr =>
            {
                Image = (curr.isChecked ? _saturated : _desaturated)[curr.frameCounter & 1];
                QuestImage = curr.isChecked ? null : (curr.isAvailable ? yellowQuestFrames : grayQuestFrames)[curr.frameCounter & 1];
            });
    }

    public string LocationKey { get; }

    public LocationDefinitionModel Model { get; }

    public Point CanvasLocation { get; }

    [Reactive]
    public Bitmap? Image { get; private set; }

    [Reactive]
    public Bitmap? QuestImage { get; private set; }

    [Reactive]
    public bool Available { get; set; }

    [Reactive]
    public bool Checked { get; set; }

    [Reactive]
    internal long FrameCounter { get; private set; }

    public void Dispose()
    {
        _clearAvailableSubscription.Dispose();
        _updateImagesSubscription.Dispose();
        foreach (Bitmap saturated in _saturated)
        {
            saturated.Dispose();
        }

        foreach (Bitmap desaturated in _desaturated)
        {
            desaturated.Dispose();
        }
    }

    internal void NextFrame()
    {
        ++FrameCounter;
    }

    private static (Bitmap[] Saturated, Bitmap[] Desaturated) ReadFrames(string locationKey)
    {
        using Stream data = AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{locationKey}.webp"));
        using SKCodec codec = SKCodec.Create(data);
        SKImageInfo imageInfo = codec.Info;
        SKCodecFrameInfo[] frameInfo = codec.FrameInfo;
        Bitmap[] saturated = new Bitmap[2];
        Bitmap[] desaturated = new Bitmap[2];
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
            desaturated[i] = ToDesaturated(bmp);
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
            desaturated[0] = desaturated[1] = ToDesaturated(bmp);
        }

        return (saturated, desaturated);
    }
}
