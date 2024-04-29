using System.Globalization;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

using Avalonia.Controls;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Autopelago.ViewModels;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsSerializerContext : JsonSerializerContext;

public sealed record Settings
{
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
        JsonTypeInfo<Settings> typeInfo = (JsonTypeInfo<Settings>)SettingsSerializerContext.Default.GetTypeInfo(typeof(Settings))!;
        FileInfo settingsFile = null!;
        if (!Design.IsDesignMode)
        {
            settingsFile = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autopelago", "lastSettings.json"));
            try
            {
                using FileStream settingsStream = settingsFile.OpenRead();
                Settings lastSettings = JsonSerializer.Deserialize(settingsStream, typeInfo) ?? throw new JsonException();
                Host = lastSettings.Host;
                Port = lastSettings.Port;
                Slot = lastSettings.Slot;
                Password = lastSettings.Password;
                MinStepSeconds = lastSettings.MinStepSeconds;
                MaxStepSeconds = lastSettings.MaxStepSeconds;
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
            }
        }

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
        ConnectCommand = ReactiveCommand.Create(() =>
        {
            Settings newSettings = new()
            {
                Host = Host,
                Port = (ushort)Port.GetValueOrDefault(),
                Slot = Slot,
                Password = Password,
                MinStepSeconds = MinStepSeconds,
                MaxStepSeconds = MaxStepSeconds,
            };

            if (!Design.IsDesignMode)
            {
                try
                {
                    settingsFile.Directory!.Create();
                    FileInfo tmpSettingsFile = new(Path.Combine(settingsFile.DirectoryName!, "tmp.json"));
                    using (FileStream settingsStream = tmpSettingsFile.OpenWrite())
                    {
                        JsonSerializer.Serialize(settingsStream, newSettings, typeInfo);
                    }

                    tmpSettingsFile.MoveTo(settingsFile.FullName, overwrite: true);
                }
                catch (IOException)
                {
                }
                catch (JsonException)
                {
                }
            }

            return newSettings;
        }, canConnect);

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

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    [GeneratedRegex(@"\:(?<port>\d+)$", RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking | RegexOptions.CultureInvariant)]
    private static partial Regex HasPortRegex();
}
