using Avalonia.Media.Imaging;
using Avalonia.Platform;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using SkiaSharp;

namespace Autopelago.ViewModels;

public sealed partial class CollectableItemViewModel : ViewModelBase, IDisposable
{
    private readonly IDisposable _updateImageSubscription;

    private readonly Bitmap _saturatedImage;

    private readonly Bitmap _desaturatedImage;

    [Reactive] private bool _collected;

    [Reactive] private Bitmap? _image;

    public CollectableItemViewModel(string itemKey)
    {
        ItemKey = itemKey;
        try
        {
            Model = GameDefinitions.Instance.ProgressionItemsByItemKey[itemKey];
        }
        catch (KeyNotFoundException ex)
        {
            throw new KeyNotFoundException($"Item Key: '{itemKey}'", ex);
        }

        _saturatedImage = new(AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{itemKey}.webp")));
        using SKBitmap bmp = SKBitmap.Decode(AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{itemKey}.webp")));
        bmp.SetImmutable();
        _desaturatedImage = ToAvaloniaDesaturated(bmp);

        _updateImageSubscription = this
            .WhenAnyValue(x => x.Collected)
            .Subscribe(collected => Image = collected ? _saturatedImage : _desaturatedImage);
    }

    public void Dispose()
    {
        _updateImageSubscription.Dispose();
        _saturatedImage.Dispose();
        _desaturatedImage.Dispose();
    }

    public string ItemKey { get; }

    public ItemDefinitionModel Model { get; }
}
