using System.Text.RegularExpressions;

namespace MetroidRescue;

internal sealed record FastbootDevice(
    string Serial,
    string Product,
    string Slot,
    bool Unlocked,
    FastbootMode Mode,
    string Bootloader,
    string OsVersion)
{
    public bool IsMetroid => Product.Equals("metroid", StringComparison.OrdinalIgnoreCase);
    public bool Userspace => Mode == FastbootMode.Userspace;
}

internal enum FastbootMode { Unknown, Bootloader, Userspace }

internal sealed class FastbootService
{
    private readonly Action<string> _log;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<CommandResult>>? _runner;
    public string FastbootPath => ToolPaths.Fastboot;

    public FastbootService(Action<string> log, Func<IReadOnlyList<string>, CancellationToken, Task<CommandResult>>? runner = null)
    {
        _log = log;
        _runner = runner;
    }

    public async Task<FastbootDevice?> DetectAsync(CancellationToken token = default) => await DetectAsync(null, token);

    public async Task<FastbootDevice?> DetectAsync(string? expectedSerial, CancellationToken token = default)
    {
        EnsureTools();
        var devices = await RunAsync(["devices"], token);
        var serials = ParseDeviceSerials(devices.Output);
        var serial = expectedSerial is null
            ? serials.Count switch { 0 => null, 1 => serials[0], _ => throw new InvalidOperationException("Multiple fastboot devices detected. Connect only the Phone (3) being recovered.") }
            : serials.FirstOrDefault(value => value.Equals(expectedSerial, StringComparison.OrdinalIgnoreCase));
        if (serial is null) return null;

        var vars = await RunAsync(["-s", serial, "getvar", "all"], token);
        if (!vars.Success) throw new InvalidOperationException("Could not query required fastboot variables.");
        string Get(string key) => Regex.Match(vars.Output, $@"(?:\(bootloader\)\s*)?{Regex.Escape(key)}:\s*([^\r\n]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        var unlockedText = Get("unlocked");
        return new FastbootDevice(
            serial,
            Get("product"),
            Get("current-slot").Replace("_", ""),
            unlockedText.Equals("yes", StringComparison.OrdinalIgnoreCase),
            Get("is-userspace").ToLowerInvariant() switch { "yes" => FastbootMode.Userspace, "no" => FastbootMode.Bootloader, _ => FastbootMode.Unknown },
            Get("version-bootloader"),
            Get("version-os"));
    }

    public Task<CommandResult> RunAsync(IEnumerable<string> args, CancellationToken token = default)
    {
        var list = args.ToArray();
        if (!IsReadOnlyCommand(list)) throw new InvalidOperationException("Raw fastboot execution permits read-only queries only.");
        _log($"> fastboot {string.Join(' ', list)}");
        if (_runner is not null) return _runner(list, token);
        EnsureTools();
        return CommandRunner.RunAsync(FastbootPath, list, _log, token);
    }

    public async Task<FastbootDevice> VerifyCurrentAsync(string serial, CancellationToken token = default)
    {
        var current = await DetectAsync(serial, token) ?? throw new InvalidOperationException("Write blocked: the selected fastboot device is no longer connected.");
        if (!current.IsMetroid)
            throw new InvalidOperationException($"Write blocked: connected product is '{current.Product}', not 'metroid'.");
        if (!current.Unlocked)
            throw new InvalidOperationException("Write blocked: bootloader is not reported unlocked.");
        if (current.Mode != FastbootMode.Bootloader)
            throw new InvalidOperationException("Write blocked: exact bootloader Fastboot mode was not confirmed.");
        return current;
    }

    public async Task RequireMetroidAsync(FastbootDevice device, bool requireUnlocked = false, CancellationToken token = default)
    {
        var current = await DetectAsync(device.Serial, token) ?? throw new InvalidOperationException("The selected fastboot device is no longer connected.");
        if (!current.IsMetroid) throw new InvalidOperationException($"Connected product is '{current.Product}', not 'metroid'.");
        if (requireUnlocked && !current.Unlocked) throw new InvalidOperationException("Bootloader is not reported unlocked.");
    }

    public async Task<CommandResult> RunRecoveryCommandAsync(string serial, IEnumerable<string> args, CancellationToken token = default)
    {
        var list = args.ToArray();
        if (!IsAllowedRecoveryCommand(list)) throw new InvalidOperationException("Command is not allowed by the conservative recovery plan.");
        await VerifyCurrentAsync(serial, token);
        token.ThrowIfCancellationRequested();
        return await RunRawAsync(["-s", serial, .. list]);
    }

    public async Task<FastbootDevice> WaitForModeAsync(string serial, bool userspace, TimeSpan timeout, CancellationToken token = default)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            token.ThrowIfCancellationRequested();
            var current = await DetectAsync(serial, token);
            if (current is not null && current.IsMetroid && current.Userspace == userspace) return current;
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
        throw new TimeoutException($"Phone did not enter {(userspace ? "fastbootd" : "bootloader fastboot")} mode.");
    }

    internal static IReadOnlyList<string> ParseDeviceSerials(string output) => output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        .Where(parts => parts.Length >= 2 && parts[1].Equals("fastboot", StringComparison.OrdinalIgnoreCase))
        .Select(parts => parts[0])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    internal static bool IsAllowedRecoveryCommand(IReadOnlyList<string> args)
    {
        if (args.Count == 1 && args[0] == "reboot") return true;
        if (args.Count == 2 && args[0] == "set_active" && args[1] == ConservativeRecoveryPlan.TargetSlot) return true;
        return args.Count == 3 && args[0] == "flash" && ConservativeRecoveryPlan.Writes.Any(write =>
            write.Target == args[1] && Path.GetFullPath(args[2]).Equals(Path.GetFullPath(ConservativeRecoveryPlan.ImagePath(write.Image)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    }

    private static bool IsReadOnlyCommand(IReadOnlyList<string> args) =>
        args.SequenceEqual(["devices"]) || args.SequenceEqual(["devices", "-l"]) ||
        args.Count == 4 && args[0] == "-s" && args[2] == "getvar" && !string.IsNullOrWhiteSpace(args[1]) && !string.IsNullOrWhiteSpace(args[3]);

    private Task<CommandResult> RunRawAsync(IReadOnlyList<string> args, CancellationToken token = default)
    {
        _log($"> fastboot {string.Join(' ', args)}");
        if (_runner is not null) return _runner(args, token);
        EnsureTools();
        return CommandRunner.RunAsync(FastbootPath, args, _log, token);
    }

    private void EnsureTools()
    {
        if (_runner is not null) return;
        ToolPaths.EnsureExecutableBits();
        if (!File.Exists(FastbootPath))
            throw new FileNotFoundException("Bundled fastboot.exe is missing.", FastbootPath);
    }
}
