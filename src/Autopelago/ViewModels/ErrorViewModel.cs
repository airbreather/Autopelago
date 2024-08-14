using System.Reactive;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Autopelago.ViewModels;

public sealed class ErrorViewModel : ViewModelBase
{
    public required ReactiveCommand<Unit, Unit> BackToMainMenuCommand { get; init; }

    [Reactive]
    public string? Error { get; set; }
}
