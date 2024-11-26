using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Autopelago.BespokeMultiworld;

public static partial class Generator
{
    public static async Task<Multiworld> BuildAsync(Prng.State seed, CancellationToken cancellationToken)
    {
        int slotCount = Directory.EnumerateFiles(Paths.InputYamlFiles).Count();

        Prng.State prngState = seed;
        UInt128 archipelagoSeed = new(Prng.Next(ref prngState), Prng.Next(ref prngState));

        ImmutableArray<World> slots;
        {
            World[] slotsMutable = new World[slotCount];
            for (int i = 0; i < slotsMutable.Length; i++)
            {
                slotsMutable[i] = new(prngState);
                Prng.ShortJump(ref prngState);
            }

            slots = ImmutableCollectionsMarshal.AsImmutableArray(slotsMutable);
        }

        byte[] spoilerLogData = await GenerateSpoilerLogForRunAsync(archipelagoSeed, cancellationToken);
        FrozenDictionary<WorldLocation, WorldItem> fullSpoilerData = ParseSpoilerLog(spoilerLogData);
        return new()
        {
            Slots = slots,
            FullSpoilerData = fullSpoilerData,
        };
    }

    private static FrozenDictionary<WorldLocation, WorldItem> ParseSpoilerLog(byte[] spoilerLogData)
    {
        using MemoryStream ms = new(spoilerLogData);
        using StreamReader rd = new(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        string[] playerNames = new string[ReadPlayerCount()];
        for (int i = 0; i < playerNames.Length; i++)
        {
            playerNames[i] = ReadPlayerName(i);
        }

        return ReadSpoilerData()
            .ToFrozenDictionary(tup => tup.Location, tup => tup.Item);
        int ReadPlayerCount()
        {
            while (true)
            {
                if (rd.ReadLine() is not string line)
                {
                    throw new InvalidDataException("Spoiler log format changed on us.");
                }

                if (line.StartsWith("Players:", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = line.Length - 1; i >= 0; i--)
                    {
                        if (!char.IsAsciiDigit(line[i]))
                        {
                            return int.Parse(line.AsSpan(i + 1));
                        }
                    }
                }
            }
        }

        string ReadPlayerName(int slot)
        {
            string start = $"Player {slot + 1}: ";
            while (true)
            {
                if (rd.ReadLine() is not string line)
                {
                    throw new InvalidDataException("Spoiler log format changed on us.");
                }

                if (line.StartsWith(start, StringComparison.OrdinalIgnoreCase))
                {
                    return line[start.Length..];
                }
            }
        }

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
            FrozenDictionary<string, int> slotByPlayer = Enumerable.Range(0, playerNames.Length).ToFrozenDictionary(i => playerNames[i], i => i);
            while (prevLine is not null && LocationLine().Match(prevLine) is { Success: true } match)
            {
                LocationKey location = GameDefinitions.Instance.LocationsByName[match.Groups["locationName"].Value].Key;
                int locationSlot = slotByPlayer[match.Groups["locationPlayer"].Value];
                string itemName = match.Groups["itemName"].Value;
                int itemSlot = slotByPlayer[match.Groups["itemPlayer"].Value];
                yield return (
                    new() { Slot = locationSlot, Location = location },
                    new() { Slot = itemSlot, ItemName = itemName }
                );

                prevLine = rd.ReadLine();
            }
        }
    }

    private static async Task<byte[]> GenerateSpoilerLogForRunAsync(UInt128 archipelagoSeed, CancellationToken cancellationToken)
    {
        using Process p = Process.Start(GetProcessStartInfo(archipelagoSeed))
            ?? throw new InvalidOperationException("Failed to start process");

        await p.WaitForExitAsync(cancellationToken);
        foreach (string zipFile in Directory.EnumerateFiles(Paths.TempForSeed(archipelagoSeed), Paths.ZipFilePattern))
        {
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
        }

        throw new InvalidOperationException("Could not generate a spoiler log for some reason.");
    }

    private static ProcessStartInfo GetProcessStartInfo(UInt128 archipelagoSeed)
    {
        return new()
        {
            FileName = Paths.PythonExe,
            WorkingDirectory = Paths.ArchipelagoSource,
            ArgumentList =
            {
                "Generate.py",
                "--seed", $"{archipelagoSeed}",
                "--player_files_path", Paths.InputYamlFiles,
                "--outputpath", Directory.CreateDirectory(Paths.TempForSeed(archipelagoSeed)).FullName,
            },
        };
    }

    private static class Paths
    {
        private static readonly string s_sciencePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "SCIENCE",
            "Autopelago"
        );

        private static readonly string s_staticPath = Path.Combine(
            s_sciencePath,
            "static"
        );

        private static readonly string s_tempPathBase = Path.Combine(
            s_sciencePath,
            "temp"
        );

        private static readonly string s_resultsPathBase = Path.Combine(
            s_sciencePath,
            "output"
        );

        public static string PythonExe { get; } = Path.Combine(s_staticPath, "venv", "bin", "python");

        public static string ArchipelagoSource { get; } = Path.Combine(s_staticPath, "Archipelago");

        public static string InputYamlFiles { get; } = Path.Combine(s_staticPath, "input-yaml-files");

        public static string ZipFilePattern { get; } = "AP_*.zip";

        public static string TempForSeed(UInt128 archipelagoSeed)
        {
            return Path.Combine(s_tempPathBase, $"{archipelagoSeed}");
        }
    }

    [GeneratedRegex(@"^(?<locationName>.+) \((?<locationPlayer>.+)\): (?<itemName>.+) \((?<itemPlayer>.+)\)$", RegexOptions.Compiled)]
    private static partial Regex LocationLine();
}
