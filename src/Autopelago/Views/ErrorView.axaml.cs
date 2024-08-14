using Autopelago.ViewModels;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.ReactiveUI;

namespace Autopelago.Views;

public sealed partial class ErrorView : ReactiveUserControl<ErrorViewModel>
{
    public ErrorView()
    {
        InitializeComponent();
    }

    private async void CopyErrorToClipboard(object? o, RoutedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && ViewModel is { } viewModel)
        {
            await clipboard.SetTextAsync(viewModel.Error);
        }
    }
}
