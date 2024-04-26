using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
        JsonTypeInfo<Settings> typeInfo = (JsonTypeInfo<Settings>)SettingsSerializerContext.Default.GetTypeInfo(typeof(Settings))!;
        FileInfo settingsFile = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autopelago", "lastSettings.json"));
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

        IObservable<bool> canConnect = this.WhenAnyValue(
            x => x.Host, x => x.Port, x => x.Slot, x => x.MinStepSeconds, x => x.MaxStepSeconds,
            (host, port, slot, minStepSeconds, maxStepSeconds) =>
                !string.IsNullOrWhiteSpace(host) &&
                port > 0 &&
                !string.IsNullOrWhiteSpace(slot) &&
                minStepSeconds > 0 &&
                maxStepSeconds >= minStepSeconds)
            .DistinctUntilChanged();
        ConnectCommand = ReactiveCommand.Create(() =>
        {
            Settings newSettings = new()
            {
                Host = Host,
                Port = Port,
                Slot = Slot,
                Password = Password,
                MinStepSeconds = MinStepSeconds,
                MaxStepSeconds = MaxStepSeconds,
            };

            try
            {
                settingsFile.Directory!.Create();
                FileInfo tmpSettingsFile = new(Path.Combine(settingsFile.Directory.FullName, "tmp.json"));
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

            return newSettings;
        }, canConnect);
    }

    public ReactiveCommand<Unit, Settings> ConnectCommand { get; }

    [Reactive]
    public string Host { get; set; } = "archipelago.gg";

    [Reactive]
    public ushort Port { get; set; } = ushort.MaxValue;

    [Reactive]
    public string Slot { get; set; } = "";

    [Reactive]
    public string Password { get; set; } = "";

    [Reactive]
    public decimal MinStepSeconds { get; set; } = 60;

    [Reactive]
    public decimal MaxStepSeconds { get; set; } = 90;
}
