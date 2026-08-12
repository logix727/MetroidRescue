using System.Text.RegularExpressions;

namespace MetroidRescue;

internal sealed record FastbootDevice(
    string Serial,
    string Product,
    string Slot,
    bool Unlocked,
    bool Userspace,
    string Bootloader,
    string OsVersion)
{
    public bool IsMetroid => Product.Equals("metroid", StringComparison.OrdinalIgnoreCase);
}

internal sealed class FastbootService
{
    private readonly Action<string> _log;
    public string FastbootPath => ToolPaths.Fastboot;

    public FastbootService(Action<string> log) => _log = log;

    public async Task<FastbootDevice?> DetectAsync(CancellationToken token = default)
    {
        EnsureTools();
        var devices = await RunAsync(["devices"], token);
        var serial = devices.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (serial is null) return null;

        var vars = await RunAsync(["-s", serial, "getvar", "all"], token);
        string Get(string key) => Regex.Match(vars.Output, $@"(?:\(bootloader\)\s*)?{Regex.Escape(key)}:\s*([^\r\n]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        var unlockedText = Get("unlocked");
        var secureText = Get("secure");
        return new FastbootDevice(
            serial,
            Get("product"),
            Get("current-slot").Replace("_", ""),
            unlockedText.Equals("yes", StringComparison.OrdinalIgnoreCase) || secureText.Equals("no", StringComparison.OrdinalIgnoreCase),
            Get("is-userspace").Equals("yes", StringComparison.OrdinalIgnoreCase),
            Get("version-bootloader"),
            Get("version-baseband"));
    }

    public Task<CommandResult> RunAsync(IEnumerable<string> args, CancellationToken token = default)
    {
        EnsureTools();
        _log($"> fastboot {string.Join(' ', args)}");
        return CommandRunner.RunAsync(FastbootPath, args, _log, token);
    }

    public async Task RequireMetroidAsync(FastbootDevice device, bool requireUnlocked = false)
    {
        if (!device.IsMetroid)
            throw new InvalidOperationException($"Write blocked: connected product is '{device.Product}', not 'metroid'.");
        if (requireUnlocked && !device.Unlocked)
            throw new InvalidOperationException("Write blocked: bootloader is not reported unlocked.");
        await Task.CompletedTask;
    }

    private void EnsureTools()
    {
        ToolPaths.EnsureExecutableBits();
        if (!File.Exists(FastbootPath))
            throw new FileNotFoundException("Bundled fastboot.exe is missing.", FastbootPath);
    }
}
