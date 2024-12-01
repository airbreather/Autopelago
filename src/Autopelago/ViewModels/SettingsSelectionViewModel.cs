using System.Globalization;
using System.Reactive;
using System.Reactive.Disposables;
using System.Text.RegularExpressions;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Autopelago.ViewModels;

public sealed partial class SettingsSelectionViewModel : ViewModelBase, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];

    private readonly IObservable<bool> _canConnect;

    [Reactive] private string _host = "archipelago.gg";

    [Reactive] private decimal? _port = ushort.MaxValue;

    [Reactive(SetModifier = AccessModifier.Private)] private bool _userCanEditPort;

    [Reactive] private string _slot = "";

    [Reactive] private string _password = "";

    [Reactive] private decimal _minStepSeconds = 60;

    [Reactive] private decimal _maxStepSeconds = 90;

    [Reactive] private bool _ratChat = true;

    public SettingsSelectionViewModel()
    {
        _canConnect = this.WhenAnyValue(
            x => x.Host, x => x.Port, x => x.Slot, x => x.MinStepSeconds, x => x.MaxStepSeconds,
            (host, port, slot, minStepSeconds, maxStepSeconds) =>
                !string.IsNullOrWhiteSpace(host) &&
                port is decimal portValue and > 0 and <= ushort.MaxValue &&
                portValue - decimal.Round(portValue) == 0 &&
                !string.IsNullOrWhiteSpace(slot) &&
                minStepSeconds > 0 &&
                maxStepSeconds >= minStepSeconds);

        _subscriptions.Add(this
            .WhenAnyValue(x => x.Host)
            .Subscribe(host =>
            {
                Match m = HasPortRegex().Match(host);
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

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    public Settings SettingsModel
    {
        get => new()
        {
            Host = HasPortRegex().Replace(Host, ""),
            Port = (ushort)Port.GetValueOrDefault(),
            Slot = Slot,
            Password = Password,
            MinStepSeconds = MinStepSeconds,
            MaxStepSeconds = MaxStepSeconds,
            RatChat = RatChat,
        };
        set
        {
            Host = value.Host;
            Port = value.Port;
            Slot = value.Slot;
            Password = value.Password;
            MinStepSeconds = value.MinStepSeconds;
            MaxStepSeconds = value.MaxStepSeconds;
            RatChat = value.RatChat;
        }
    }

    [GeneratedRegex(@"\:(?<port>\d+)$", RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking | RegexOptions.CultureInvariant)]
    private static partial Regex HasPortRegex();

    [ReactiveCommand(CanExecute = nameof(_canConnect))]
    private Settings Connect(Unit unit)
    {
        return SettingsModel;
    }
}
