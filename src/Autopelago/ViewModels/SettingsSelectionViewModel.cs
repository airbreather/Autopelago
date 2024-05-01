using System.Globalization;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Autopelago.ViewModels;

public sealed record Settings
{
    public static readonly Settings Default = new()
    {
        Host = "archipelago.gg",
        Port = 65535,
        Slot = "",
        Password = "",
        MinStepSeconds = 60,
        MaxStepSeconds = 90,
    };

    public static readonly Settings ForDesigner = new()
    {
        Host = "UI DESIGNER",
        Port = 65535,
        Slot = "Ratthew",
        Password = "",
        MinStepSeconds = 1,
        MaxStepSeconds = 1,
    };

    public required string Host { get; init; }

    public required ushort Port { get; init; }

    public required string Slot { get; init; }

    [JsonIgnore]
    public string Password { get; init; } = "";

    public required decimal MinStepSeconds { get; init; }

    public required decimal MaxStepSeconds { get; init; }
}

public sealed partial class SettingsSelectionViewModel : ViewModelBase, IDisposable
{
    private static readonly Regex s_portRegex = HasPortRegex();

    private readonly CompositeDisposable _subscriptions = [];

    public SettingsSelectionViewModel()
    {
        IObservable<bool> canConnect = this.WhenAnyValue(
            x => x.Host, x => x.Port, x => x.Slot, x => x.MinStepSeconds, x => x.MaxStepSeconds,
            (host, port, slot, minStepSeconds, maxStepSeconds) =>
                !string.IsNullOrWhiteSpace(host) &&
                port is decimal portValue and > 0 and <= ushort.MaxValue &&
                portValue - decimal.Round(portValue) == 0 &&
                !string.IsNullOrWhiteSpace(slot) &&
                minStepSeconds > 0 &&
                maxStepSeconds >= minStepSeconds)
            .DistinctUntilChanged();
        ConnectCommand = ReactiveCommand.Create(() => SettingsModel, canConnect);

        _subscriptions.Add(this
            .WhenAnyValue(x => x.Host)
            .Subscribe(host =>
            {
                Match m = s_portRegex.Match(host);
                if (!m.Success)
                {
                    UserCanEditPort = true;
                    return;
                }

                UserCanEditPort = false;
                Port = decimal.TryParse(m.Groups["port"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out decimal port)
                    ? port
                    : 65535;
            }));
    }

    public ReactiveCommand<Unit, Settings> ConnectCommand { get; }

    [Reactive]
    public string Host { get; set; } = "archipelago.gg";

    [Reactive]
    public decimal? Port { get; set; } = ushort.MaxValue;

    [Reactive]
    public bool UserCanEditPort { get; private set; }

    [Reactive]
    public string Slot { get; set; } = "";

    [Reactive]
    public string Password { get; set; } = "";

    [Reactive]
    public decimal MinStepSeconds { get; set; } = 60;

    [Reactive]
    public decimal MaxStepSeconds { get; set; } = 90;

    public Settings SettingsModel
    {
        get => new()
        {
            Host = s_portRegex.Replace(Host, ""),
            Port = (ushort)Port.GetValueOrDefault(),
            Slot = Slot,
            Password = Password,
            MinStepSeconds = MinStepSeconds,
            MaxStepSeconds = MaxStepSeconds,
        };
        set
        {
            Host = value.Host;
            Port = value.Port;
            Slot = value.Slot;
            Password = value.Password;
            MinStepSeconds = value.MinStepSeconds;
            MaxStepSeconds = value.MaxStepSeconds;
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    [GeneratedRegex(@"\:(?<port>\d+)$", RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking | RegexOptions.CultureInvariant)]
    private static partial Regex HasPortRegex();
}
