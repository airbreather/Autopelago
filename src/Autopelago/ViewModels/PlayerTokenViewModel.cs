using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using SkiaSharp;

namespace Autopelago.ViewModels;

public sealed partial class PlayerTokenViewModel : ViewModelBase, IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    [Reactive] private Color _color = Color.Parse("#382E26");

    [Reactive] private PlayerTokenKind _kind = PlayerTokenKind.Player1;

    [ObservableAsProperty] private bool _isPlayer1;

    [ObservableAsProperty] private bool _isPlayer2;

    [ObservableAsProperty] private bool _isPlayer4;

    [ObservableAsProperty] private WriteableBitmap _playerTokenIconSource = null!;

    public PlayerTokenViewModel()
    {
        IObservable<PlayerTokenKind> playerTokenChanges =
            this.ObservableForProperty(x => x.Kind, skipInitial: false)
                .Select(p => p.Value);

        _playerTokenIconSourceHelper = playerTokenChanges
            .Select(p => p switch
            {
                PlayerTokenKind.Player1 => Player1,
                PlayerTokenKind.Player2 => Player2,
                PlayerTokenKind.Player4 => Player4,
            })
            .ToProperty(this, x => x.PlayerTokenIconSource)
            .DisposeWith(_disposables);

        _isPlayer1Helper = playerTokenChanges
            .Select(p => p == PlayerTokenKind.Player1)
            .ToProperty(this, x => x.IsPlayer1)
            .DisposeWith(_disposables);

        _isPlayer2Helper = playerTokenChanges
            .Select(p => p == PlayerTokenKind.Player2)
            .ToProperty(this, x => x.IsPlayer2)
            .DisposeWith(_disposables);

        _isPlayer4Helper = playerTokenChanges
            .Select(p => p == PlayerTokenKind.Player4)
            .ToProperty(this, x => x.IsPlayer4)
            .DisposeWith(_disposables);

        this.ObservableForProperty(x => x.Color)
            .Subscribe(c =>
            {
                SKColor lightColor = new(c.Value.ToUInt32());
                PlayerTokens.DrawTo(Player1, PlayerTokenKind.Player1, lightColor);
                PlayerTokens.DrawTo(Player2, PlayerTokenKind.Player2, lightColor);
                PlayerTokens.DrawTo(Player4, PlayerTokenKind.Player4, lightColor);
            })
            .DisposeWith(_disposables);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    [ReactiveCommand]
    private void ChoosePlayerToken(PlayerTokenKind kind)
    {
        Kind = kind;
    }

    public WriteableBitmap Player1 { get; } = WriteableBitmap.Decode(AssetLoader.Open(new("avares://Autopelago/Assets/Images/pack_rat.webp")));

    public WriteableBitmap Player2 { get; } = WriteableBitmap.Decode(AssetLoader.Open(new("avares://Autopelago/Assets/Images/player2.webp")));

    public WriteableBitmap Player4 { get; } = WriteableBitmap.Decode(AssetLoader.Open(new("avares://Autopelago/Assets/Images/player4.webp")));

    public ICommand? ClosePaneCommand { get; init; }

    [ReactiveCommand]
    private void Reset()
    {
        Kind = PlayerTokenKind.Player1;
        Color = Color.Parse("#FF382E26");
    }
}
