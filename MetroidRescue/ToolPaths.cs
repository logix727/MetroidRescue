using System.Runtime.InteropServices;

namespace MetroidRescue;

internal static class ToolPaths
{
    private static string PlatformDirectory => Path.Combine(AppContext.BaseDirectory, "Tools", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-x64" : "linux-x64");
    public static string Adb => Path.Combine(PlatformDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "adb.exe" : "adb");
    public static string Fastboot => Path.Combine(PlatformDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "fastboot.exe" : "fastboot");
    public static string PayloadDumper => Path.Combine(PlatformDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "payload-dumper-go.exe" : "payload-dumper-go");
    public static string WindowsDriver => Path.Combine(AppContext.BaseDirectory, "Tools", "windows-driver", "android_winusb.inf");

    public static void EnsureExecutableBits()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        foreach (var path in new[] { Adb, Fastboot, PayloadDumper })
        {
            if (!File.Exists(path)) continue;
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
}
