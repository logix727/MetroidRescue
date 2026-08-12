using System.Text.Json;

namespace MetroidRescue;

internal sealed record RecoveryReport(DateTime CompletedUtc, string Outcome, string Serial, string FirmwareBuild, string FirmwareSha256, bool Wiped, string Detail, IReadOnlyList<string> Warnings);

internal sealed class RecoveryReportService
{
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroidRescue", "reports");

    public async Task<string> WriteAsync(string outcome, FastbootDevice device, FirmwareInfo firmware, bool wiped, string detail, IEnumerable<string>? warnings = null)
    {
        Directory.CreateDirectory(_directory);
        var report = new RecoveryReport(DateTime.UtcNow, outcome, device.Serial, firmware.Build, firmware.Sha256, wiped, detail, warnings?.ToArray() ?? []);
        var path = Path.Combine(_directory, $"Recovery-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}
