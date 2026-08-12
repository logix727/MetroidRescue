namespace MetroidRescue;

internal sealed record DriverReport(bool FastbootVisible, bool AndroidDevicePresent, string Details);

internal sealed class DriverDiagnostics
{
    private readonly Action<string> _log;
    public DriverDiagnostics(Action<string> log) => _log = log;

    public async Task<DriverReport> InspectAsync(CancellationToken token = default)
    {
        var pnputil = await CommandRunner.RunAsync("pnputil.exe", ["/enum-devices", "/connected"], cancellationToken: token);
        var text = pnputil.Output;
        var androidPresent = text.Contains("Android", StringComparison.OrdinalIgnoreCase) || text.Contains("18D1", StringComparison.OrdinalIgnoreCase);
        var fastboot = text.Contains("Bootloader", StringComparison.OrdinalIgnoreCase) || text.Contains("Fastboot", StringComparison.OrdinalIgnoreCase);
        var details = fastboot ? "Android bootloader driver appears active." : androidPresent ? "Android USB device found, but bootloader driver is not confirmed." : "No connected Android USB device appears in Windows driver inventory.";
        _log("Driver diagnostics: " + details);
        return new DriverReport(fastboot, androidPresent, details);
    }
}
