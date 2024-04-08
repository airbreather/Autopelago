using Avalonia.Media.Imaging;
using Avalonia.Platform;

using ReactiveUI.Fody.Helpers;

using SkiaSharp;

namespace Autopelago.ViewModels;

public sealed class CollectableItemViewModel : ViewModelBase, IDisposable
{
    public CollectableItemViewModel(string itemKey)
    {
        ItemKey = itemKey;
        Model = GameDefinitions.Instance.ProgressionItems[itemKey];
        Image = new(AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{itemKey}.webp")));

        using SKBitmap bmp = SKBitmap.Decode(AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{itemKey}.webp")));
        DesaturatedImage = ToDesaturated(bmp);
    }

    public string ItemKey { get;  }

    public ItemDefinitionModel Model { get; }

    [Reactive]
    public bool Collected { get; set; }

    public Bitmap Image { get; }

    public Bitmap DesaturatedImage { get; }

    public void Dispose()
    {
        Image.Dispose();
        DesaturatedImage.Dispose();
    }
}
