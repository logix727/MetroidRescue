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
}
