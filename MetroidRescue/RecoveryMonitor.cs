namespace MetroidRescue;

internal enum DeviceTransport
{
    Missing,
    Adb,
    Fastboot,
    AdbUnauthorized,
}

internal sealed class RecoveryMonitor
{
    private readonly Action<string> _log;
    private string AdbPath => ToolPaths.Adb;
    private string FastbootPath => ToolPaths.Fastboot;

    public RecoveryMonitor(Action<string> log) => _log = log;

    public async Task<DeviceTransport> WaitForBootAsync(string serial, TimeSpan timeout, Action<int> progress, CancellationToken token)
    {
        var started = DateTime.UtcNow;
        var lastTransport = DeviceTransport.Missing;
        while (DateTime.UtcNow - started < timeout)
        {
            token.ThrowIfCancellationRequested();
            var adb = await CommandRunner.RunAsync(AdbPath, ["devices"], cancellationToken: token);
            if (HasTransport(adb.Output, serial, "unauthorized"))
            {
                _log("Android is running, but ADB authorization is pending on the phone.");
                progress(100);
                return DeviceTransport.AdbUnauthorized;
            }
            if (HasTransport(adb.Output, serial, "device"))
            {
                _log("Android answered through ADB. Rescue succeeded.");
                progress(100);
                return DeviceTransport.Adb;
            }

            var fastboot = await CommandRunner.RunAsync(FastbootPath, ["devices"], cancellationToken: token);
            var transport = FastbootService.ParseDeviceSerials(fastboot.Output).Contains(serial, StringComparer.OrdinalIgnoreCase) ? DeviceTransport.Fastboot : DeviceTransport.Missing;
            if (transport != lastTransport)
            {
                _log(transport == DeviceTransport.Fastboot ? "Phone returned to fastboot." : "Waiting for Android to start...");
                lastTransport = transport;
            }

            var percent = (int)Math.Min(99, (DateTime.UtcNow - started).TotalMilliseconds / timeout.TotalMilliseconds * 100);
            progress(percent);
            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }
        return lastTransport;
    }

    internal static bool HasTransport(string output, string serial, string state) => output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        .Any(parts => parts.Length >= 2 && parts[0].Equals(serial, StringComparison.OrdinalIgnoreCase) && parts[1].Equals(state, StringComparison.OrdinalIgnoreCase));
}
