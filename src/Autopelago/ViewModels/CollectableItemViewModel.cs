using Avalonia.Media.Imaging;
using Avalonia.Platform;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using SkiaSharp;

namespace Autopelago.ViewModels;

public sealed class CollectableItemViewModel : ViewModelBase, IDisposable
{
    private readonly IDisposable _updateImageSubscription;

    private readonly Bitmap _saturatedImage;

    private readonly Bitmap _desaturatedImage;

    public CollectableItemViewModel(string itemKey)
    {
        ItemKey = itemKey;
        try
        {
            Model = GameDefinitions.Instance.ProgressionItems[itemKey];
        }
        catch (KeyNotFoundException ex)
        {
            throw new KeyNotFoundException($"Item Key: '{itemKey}'", ex);
        }

        _saturatedImage = new(AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{itemKey}.webp")));
        using SKBitmap bmp = SKBitmap.Decode(AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{itemKey}.webp")));
        bmp.SetImmutable();
        _desaturatedImage = ToDesaturated(bmp);

        _updateImageSubscription = this
            .WhenAnyValue(x => x.Collected)
            .Subscribe(collected => Image = collected ? _saturatedImage : _desaturatedImage);
    }

    public string ItemKey { get;  }

    public ItemDefinitionModel Model { get; }

    [Reactive]
    public bool Collected { get; set; }

    public Bitmap? Image { get; set; }

    public void Dispose()
    {
        _updateImageSubscription.Dispose();
        _saturatedImage.Dispose();
        _desaturatedImage.Dispose();
    }
}
