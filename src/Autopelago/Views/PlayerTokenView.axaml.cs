using Autopelago.ViewModels;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.ReactiveUI;

namespace Autopelago.Views;

public sealed partial class PlayerTokenView : ReactiveUserControl<PlayerTokenViewModel>
{
    public PlayerTokenView()
    {
        InitializeComponent();
    }

    private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        this.ViewModel!.PlayerToken = ((Border)sender!).Name switch
        {
            "Player1" => PlayerTokenKind.Player1,
            "Player2" => PlayerTokenKind.Player2,
            "Player4" => PlayerTokenKind.Player4,
            _ => PlayerTokenKind.Player1,
        };
    }
}
