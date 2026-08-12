using System.Diagnostics;

namespace MetroidRescue;

internal sealed record CommandResult(int ExitCode, string Output)
{
    public bool Success => ExitCode == 0;
}

internal static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        Action<string>? onLine = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var output = new List<string>();
        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            throw;
        }
        return new CommandResult(process.ExitCode, string.Join(Environment.NewLine, output));

        void Capture(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (output) output.Add(line);
            onLine?.Invoke(line);
        }
    }
}
