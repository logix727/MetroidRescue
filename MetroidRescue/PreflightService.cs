using System.Globalization;
using System.Text.RegularExpressions;

namespace MetroidRescue;

internal sealed record PreflightReport(bool Passed, int? BatteryPercent, long? MaxDownloadBytes, IReadOnlyList<string> Warnings, IReadOnlyList<string> Checks);

internal sealed class PreflightService
{
    private readonly FastbootService _fastboot;
    private readonly FirmwareService _firmware;
    private readonly Action<string> _log;
    public PreflightService(FastbootService fastboot, FirmwareService firmware, Action<string> log) { _fastboot = fastboot; _firmware = firmware; _log = log; }

    public async Task<PreflightReport> RunAsync(FastbootDevice device, string targetSlot, CancellationToken token = default)
    {
        var warnings = new List<string>(); var checks = new List<string>();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var probe = await _fastboot.RunAsync(["-s", device.Serial, "getvar", "product"], token);
            var product = Regex.Match(probe.Output, @"(?:\(bootloader\)\s*)?product:\s*([^\r\n]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            if (!probe.Success || !product.Equals("metroid", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"USB stability probe {attempt}/3 failed.");
            checks.Add($"USB stability probe {attempt}/3 passed");
            await Task.Delay(350, token);
        }

        var battery = await GetNumberAsync(device, "battery-soc", token);
        if (battery is < 30) throw new InvalidOperationException($"Battery is only {battery}%. Charge to at least 30% before flashing.");
        if (battery is null) warnings.Add("Fastboot did not report battery percentage; verify adequate charge manually.");
        else checks.Add($"Battery {battery}%");

        var maxDownload = await GetHexAsync(device, "max-download-size", token);
        if (maxDownload is not null) checks.Add($"Fastboot max download {maxDownload / 1024d / 1024d:F0} MB");
        else warnings.Add("Fastboot did not report max-download-size.");

        var snapshot = await GetTextAsync(device, "snapshot-update-status", token);
        if (!IsSnapshotSafe(snapshot))
            throw new InvalidOperationException($"Virtual A/B snapshot state is '{snapshot}'. Complete or cancel the OTA merge before recovery writes.");
        checks.Add(string.IsNullOrWhiteSpace(snapshot) ? "Snapshot state not reported" : "No active Virtual A/B snapshot update");

        foreach (var partition in FirmwareService.RescuePartitions)
        {
            var target = partition + "_" + targetSlot;
            var partitionSize = await GetHexAsync(device, "partition-size:" + target, token);
            if (partitionSize is null) throw new InvalidOperationException($"Could not verify partition size for required target {target}.");
            var imageSize = new FileInfo(_firmware.ImagePath(partition)).Length;
            if (imageSize > partitionSize) throw new InvalidOperationException($"{partition}.img ({imageSize} bytes) exceeds {target} ({partitionSize} bytes).");
            checks.Add($"{target} size accepts image");
        }
        foreach (var item in checks) _log("Preflight OK: " + item);
        foreach (var item in warnings) _log("Preflight warning: " + item);
        return new PreflightReport(true, battery is null ? null : (int)battery, maxDownload, warnings, checks);
    }

    private async Task<long?> GetNumberAsync(FastbootDevice device, string variable, CancellationToken token)
    {
        var result = await _fastboot.RunAsync(["-s", device.Serial, "getvar", variable], token);
        var match = Regex.Match(result.Output, Regex.Escape(variable) + @":\s*(\d+)", RegexOptions.IgnoreCase);
        return match.Success && long.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    private async Task<long?> GetHexAsync(FastbootDevice device, string variable, CancellationToken token)
    {
        var result = await _fastboot.RunAsync(["-s", device.Serial, "getvar", variable], token);
        var match = Regex.Match(result.Output, Regex.Escape(variable) + @":\s*(0x[0-9a-f]+|\d+)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var text = match.Groups[1].Value;
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null
            : long.TryParse(text, out var value) ? value : null;
    }

    private async Task<string?> GetTextAsync(FastbootDevice device, string variable, CancellationToken token)
    {
        var result = await _fastboot.RunAsync(["-s", device.Serial, "getvar", variable], token);
        if (!result.Success) return null;
        var match = Regex.Match(result.Output, Regex.Escape(variable) + @":\s*([^\r\n]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    internal static bool IsSnapshotSafe(string? state) => state?.Equals("none", StringComparison.OrdinalIgnoreCase) == true;
}
