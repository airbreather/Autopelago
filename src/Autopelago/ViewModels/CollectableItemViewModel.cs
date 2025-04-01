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

    [ObservableAsProperty] private bool _lactoseIntolerant;

    [Reactive] private bool _collected;

    [Reactive] private bool _relevant = true;

    [Reactive] private Bitmap? _image;

    public CollectableItemViewModel(string yamlKey, IObservable<bool> lactoseIntolerant)
    {
        YamlKey = yamlKey;
        _lactoseIntolerantHelper = lactoseIntolerant
            .ToProperty(this, x => x.LactoseIntolerant);
        try
        {
            Model = GameDefinitions.Instance[GameDefinitions.Instance.ProgressionItemsByYamlKey[yamlKey]];
        }
        catch (KeyNotFoundException ex)
        {
            throw new KeyNotFoundException($"Item Key: '{yamlKey}'", ex);
        }

        _saturatedImage = new(AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{yamlKey}.webp")));
        using SKBitmap bmp = SKBitmap.Decode(AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{yamlKey}.webp")));
        bmp.SetImmutable();
        _desaturatedImage = bmp.ToAvaloniaDesaturated();

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

    public string YamlKey { get; }

    public ItemDefinitionModel Model { get; }
}
