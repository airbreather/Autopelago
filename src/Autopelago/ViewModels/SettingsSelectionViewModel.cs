using System.Reactive;

using ReactiveUI;

namespace Autopelago.ViewModels;

public sealed record Settings
{
    public required string Host { get; init; }

    public required ushort Port { get; init; }

    public required string Slot { get; init; }

    public required string Password { get; init; }

    public required decimal MinStepSeconds { get; init; }

    public required decimal MaxStepSeconds { get; init; }
}

public sealed class SettingsSelectionViewModel : ViewModelBase
{
    public SettingsSelectionViewModel()
    {
        ConnectCommand = ReactiveCommand.Create<Unit, Settings>(_ => new()
        {
            Host = _host,
            Port = _port,
            Slot = _slot,
            Password = _password,
            MinStepSeconds = _minStepSeconds,
            MaxStepSeconds = _maxStepSeconds,
        });
    }

    public ReactiveCommand<Unit, Settings> ConnectCommand { get; }

    private string _host = "archipelago.gg";
    public string Host
    {
        get => _host;
        set => this.RaiseAndSetIfChanged(ref _host, value);
    }

    private ushort _port;
    public ushort Port
    {
        get => _port;
        set => this.RaiseAndSetIfChanged(ref _port, value);
    }

    private string _slot = "";
    public string Slot
    {
        get => _slot;
        set => this.RaiseAndSetIfChanged(ref _slot, value);
    }

    private string _password = "";
    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    private decimal _minStepSeconds = 60;
    public decimal MinStepSeconds
    {
        get => _minStepSeconds;
        set => this.RaiseAndSetIfChanged(ref _minStepSeconds, value);
    }

    private decimal _maxStepSeconds = 90;
    public decimal MaxStepSeconds
    {
        get => _maxStepSeconds;
        set => this.RaiseAndSetIfChanged(ref _maxStepSeconds, value);
    }
}
