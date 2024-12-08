using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Skia.Helpers;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Autopelago.ViewModels;

public enum PlayerTokenKind
{
    Player1,
    Player2,
    Player4,
}

public sealed partial class PlayerTokenViewModel : ViewModelBase
{
    [Reactive] private Color _color = Color.Parse("#382E26");

    [Reactive] private PlayerTokenKind _playerToken = PlayerTokenKind.Player1;

    [ObservableAsProperty] private bool _isPlayer1;

    [ObservableAsProperty] private bool _isPlayer2;

    [ObservableAsProperty] private bool _isPlayer4;

    [ObservableAsProperty] private Bitmap? _playerTokenIconSource;

    public PlayerTokenViewModel()
    {
        IObservable<PlayerTokenKind> playerTokenChanges =
            this.ObservableForProperty(x => x.PlayerToken, skipInitial: false)
                .Select(p => p.Value);

        _playerTokenIconSourceHelper = playerTokenChanges
            .Select(p => p switch
            {
                PlayerTokenKind.Player1 => new Uri("avares://Autopelago/Assets/Images/pack_rat.webp"),
                PlayerTokenKind.Player2 => new Uri("avares://Autopelago/Assets/Images/player2.webp"),
                PlayerTokenKind.Player4 => new Uri("avares://Autopelago/Assets/Images/player4.webp"),
            })
            .Select(s => new Bitmap(AssetLoader.Open(s)))
            .ToProperty(this, x => x.PlayerTokenIconSource);

        _isPlayer1Helper = playerTokenChanges
            .Select(p => p == PlayerTokenKind.Player1)
            .ToProperty(this, x => x.IsPlayer1);

        _isPlayer2Helper = playerTokenChanges
            .Select(p => p == PlayerTokenKind.Player2)
            .ToProperty(this, x => x.IsPlayer2);

        _isPlayer4Helper = playerTokenChanges
            .Select(p => p == PlayerTokenKind.Player4)
            .ToProperty(this, x => x.IsPlayer4);
    }

    [ReactiveCommand]
    private void ChoosePlayerToken(PlayerTokenKind playerToken)
    {
        PlayerToken = playerToken;
    }

    public ICommand? ClosePaneCommand { get; init; }
}
