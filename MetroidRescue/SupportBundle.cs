using System.IO.Compression;
using System.Text;

namespace MetroidRescue;

internal static class SupportBundle
{
    public static async Task<string> CreateAsync(string destinationZip, string visibleLog, FastbootService fastboot, RescueJournal journal, FirmwareInfo? firmware)
    {
        var temp = Path.Combine(Path.GetTempPath(), "MetroidRescue-Support-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temp, "app-log.txt"), visibleLog, Encoding.UTF8);
            var diagnostic = await fastboot.RunAsync(["devices", "-l"]);
            await File.WriteAllTextAsync(Path.Combine(temp, "fastboot-devices.txt"), diagnostic.Output, Encoding.UTF8);
            if (File.Exists(journal.Path)) File.Copy(journal.Path, Path.Combine(temp, "rescue-journal.jsonl"), true);
            if (firmware is not null)
                await File.WriteAllTextAsync(Path.Combine(temp, "firmware.txt"), $"Name: {firmware.Name}\nBuild: {firmware.Build}\nSHA-256: {firmware.Sha256}\nSize: {firmware.SizeBytes}\nIncremental: {firmware.IsIncremental}\n", Encoding.UTF8);
            var provenance = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroidRescue", "images", "provenance.json");
            if (File.Exists(provenance)) File.Copy(provenance, Path.Combine(temp, "provenance.json"), true);
            var reports = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroidRescue", "reports");
            if (Directory.Exists(reports))
                foreach (var report in Directory.GetFiles(reports, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(5)) File.Copy(report, Path.Combine(temp, Path.GetFileName(report)), true);
            await File.WriteAllTextAsync(Path.Combine(temp, "system.txt"), $"OS: {Environment.OSVersion}\n.NET: {Environment.Version}\n64-bit OS: {Environment.Is64BitOperatingSystem}\n", Encoding.UTF8);
            if (File.Exists(destinationZip)) File.Delete(destinationZip);
            ZipFile.CreateFromDirectory(temp, destinationZip, CompressionLevel.SmallestSize, false);
            return destinationZip;
        }
        finally { Directory.Delete(temp, true); }
    }
}
