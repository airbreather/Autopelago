using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Autopelago.BespokeMultiworld;

public static partial class PlaythroughGenerator
{
    private static readonly string s_yamlTemplate = """
        Autopelago:
          progression_balancing: 50
          accessibility: minimal
        description: 'Generated by https://archipelago.gg/ for Autopelago'
        game: Autopelago
        name: {SlotName}
        """;

    public static async Task<ImmutableArray<FrozenDictionary<LocationKey, WorldItem>>> GenerateAsync(string scienceDir, UInt128 archipelagoSeed, int slotCount, CancellationToken cancellationToken)
    {
        byte[] spoilerLogData = await GenerateSpoilerLogForRunAsync(scienceDir, archipelagoSeed, slotCount, cancellationToken);
        using MemoryStream ms = new(spoilerLogData);
        using StreamReader rd = new(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        return
        [
            .. ReadSpoilerData()
                .GroupBy(tup => tup.Location.Slot)
                .OrderBy(grp => grp.Key)
                .Select(grp => grp.ToFrozenDictionary(tup => tup.Location.Location, tup => tup.Item)),
        ];
        IEnumerable<(WorldLocation Location, WorldItem Item)> ReadSpoilerData()
        {
            while (true)
            {
                if (rd.ReadLine() is not string line)
                {
                    throw new InvalidDataException("Spoiler log format changed on us.");
                }

                if (line != "Locations:")
                {
                    continue;
                }

                if (rd.ReadLine() != "")
                {
                    throw new InvalidDataException("Spoiler log format changed on us.");
                }

                break;
            }

            string? prevLine = rd.ReadLine();
            while (prevLine is not null && LocationLine().Match(prevLine) is { Success: true } match)
            {
                LocationKey location = GameDefinitions.Instance.LocationsByName[match.Groups["locationName"].Value].Key;
                int locationSlot = int.Parse(match.Groups["locationPlayer"].Value);
                string itemName = match.Groups["itemName"].Value;
                int itemSlot = int.Parse(match.Groups["itemPlayer"].Value);
                yield return (
                    new() { Slot = locationSlot, Location = location },
                    new() { Slot = itemSlot, ItemName = itemName }
                );

                prevLine = rd.ReadLine();
            }
        }
    }

    private static async Task<byte[]> GenerateSpoilerLogForRunAsync(string scienceDir, UInt128 archipelagoSeed, int slotCount, CancellationToken cancellationToken)
    {
        string zipFile = Paths.ResultFileForSeed(scienceDir, archipelagoSeed, slotCount);
        if (!File.Exists(zipFile))
        {
            await ActuallyGenerateAsync(scienceDir, archipelagoSeed, slotCount, cancellationToken);
        }

        using ZipArchive zip = ZipFile.OpenRead(zipFile);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!entry.FullName.EndsWith("Spoiler.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using MemoryStream ms = new();
            await using (Stream s = entry.Open())
            {
                await s.CopyToAsync(ms, cancellationToken);
            }

            return ms.ToArray();
        }

        throw new InvalidOperationException("Could not generate a spoiler log for some reason.");
    }

    private static async Task ActuallyGenerateAsync(string scienceDir, UInt128 archipelagoSeed, int slotCount, CancellationToken cancellationToken)
    {
        for (int i = 0; i < slotCount; i++)
        {
            string path = Paths.InputYamlFile(scienceDir, archipelagoSeed, slotCount, i);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, s_yamlTemplate.Replace("{SlotName}", $"Slot{i}"), Encoding.UTF8, cancellationToken);
        }

        using Process p = Process.Start(GetProcessStartInfo(scienceDir, archipelagoSeed, slotCount))
                          ?? throw new InvalidOperationException("Failed to start process");

        await p.WaitForExitAsync(cancellationToken);
        foreach (string zipFile in Directory.GetFiles(Paths.TempForSeed(scienceDir, archipelagoSeed, slotCount), Paths.ZipFilePattern))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.ResultFileForSeed(scienceDir, archipelagoSeed, slotCount))!);
            File.Move(zipFile, Paths.ResultFileForSeed(scienceDir, archipelagoSeed, slotCount));
            Directory.Delete(Paths.TempForSeed(scienceDir, archipelagoSeed, slotCount), recursive: true);
            return;
        }

        throw new InvalidOperationException("Could not generate a spoiler log for some reason.");
    }

    private static ProcessStartInfo GetProcessStartInfo(string scienceDir, UInt128 archipelagoSeed, int slotCount)
    {
        return new()
        {
            FileName = Paths.PythonExe(scienceDir),
            WorkingDirectory = Paths.ArchipelagoSource(scienceDir),
            ArgumentList =
            {
                "Generate.py",
                "--seed", $"{archipelagoSeed}",
                "--player_files_path", Paths.TempForSeed(scienceDir,archipelagoSeed, slotCount),
                "--outputpath", Directory.CreateDirectory(Paths.TempForSeed(scienceDir, archipelagoSeed, slotCount)).FullName,
            },
        };
    }

    public static class Paths
    {
        public static string PythonExe(string scienceDir) => Path.Combine(scienceDir, "static", "venv", "bin", "python");

        public static string ArchipelagoSource(string scienceDir) => Path.Combine(scienceDir, "static", "Archipelago");

        public static string ResultFileForLocationAttempts(string scienceDir) => Path.Combine(scienceDir, "output", "tables", "location-attempts.csv");

        public static string ResultFileForMovements(string scienceDir) => Path.Combine(scienceDir, "output", "tables", "movements.csv");

        public static string ZipFilePattern => "AP_*.zip";

        public static string InputYamlFile(string scienceDir, UInt128 archipelagoSeed, int slotCount, int slotNumber)
        {
            return Path.Combine(scienceDir, "temp", $"{archipelagoSeed}", $"{slotCount}", $"{slotNumber}.yml");
        }

        public static string TempForSeed(string scienceDir, UInt128 archipelagoSeed, int slotCount)
        {
            return Path.Combine(scienceDir, "temp", $"{archipelagoSeed}", $"{slotCount}");
        }

        public static string ResultFileForSeed(string scienceDir, UInt128 archipelagoSeed, int slotCount)
        {
            return Path.Combine(scienceDir, "output", "seeds", $"{slotCount}", $"{archipelagoSeed}.zip");
        }
    }

    [GeneratedRegex(@"^(?<locationName>.+) \(Slot(?<locationPlayer>\d+)\): (?<itemName>.+) \(Slot(?<itemPlayer>\d+)\)$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex LocationLine();
}