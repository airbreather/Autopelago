using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Media;

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

    [ObservableAsProperty] private Uri? _playerTokenIconSource;

    public PlayerTokenViewModel()
    {
        _playerTokenIconSourceHelper = this.ObservableForProperty(x => x.PlayerToken)
            .Select(p => p.Value switch
            {
                PlayerTokenKind.Player1 => new Uri("avares://Avalonia/Assets/Images/pack_rat.webp"),
                PlayerTokenKind.Player2 => new Uri("avares://Avalonia/Assets/Images/player2.webp"),
                PlayerTokenKind.Player4 => new Uri("avares://Avalonia/Assets/Images/player4.webp"),
            })
            .ToProperty(this, x => x.PlayerTokenIconSource);
    }

    public ICommand? ClosePaneCommand { get; init; }
}
