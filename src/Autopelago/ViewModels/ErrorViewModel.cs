using System.Reactive;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Autopelago.ViewModels;

public sealed partial class ErrorViewModel : ViewModelBase
{
    [Reactive] private string? _message;

    public required ReactiveCommand<Unit, Unit> BackToMainMenuCommand { get; init; }
}
