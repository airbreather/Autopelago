using Autopelago.ViewModels;

using Avalonia;
using Avalonia.ReactiveUI;

namespace Autopelago.Views;

public sealed partial class MainWindowView : ReactiveWindow<MainWindowViewModel>
{
    public MainWindowView()
    {
        InitializeComponent();
        this.AttachDevTools();
    }
}
