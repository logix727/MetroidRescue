using MetroidRescue;

namespace MetroidRescue.Tests;

public class CommandRunnerTests
{
    [Fact]
    public async Task CapturesFailureExitCodeAndOutput()
    {
        var executable = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var args = OperatingSystem.IsWindows() ? new[] { "/c", "echo simulated-fastboot-failure && exit /b 7" } : new[] { "-c", "echo simulated-fastboot-failure; exit 7" };
        var result = await CommandRunner.RunAsync(executable, args);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("simulated-fastboot-failure", result.Output);
    }

    [Fact]
    public async Task CancellationTerminatesChildProcessTree()
    {
        var marker = Path.Combine(Path.GetTempPath(), "MetroidRescueTests", Guid.NewGuid().ToString("N"), "orphan.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        var executable = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var command = OperatingSystem.IsWindows()
            ? $"ping -n 4 127.0.0.1 >nul & echo orphan>\"{marker}\""
            : $"sleep 3; echo orphan > '{marker}'";
        var args = OperatingSystem.IsWindows() ? new[] { "/c", command } : new[] { "-c", command };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CommandRunner.RunAsync(executable, args, cancellationToken: cancellation.Token));
        await Task.Delay(TimeSpan.FromSeconds(4));

        Assert.False(File.Exists(marker));
    }
}
