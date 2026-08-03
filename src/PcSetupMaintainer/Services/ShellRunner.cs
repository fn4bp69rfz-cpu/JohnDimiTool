using System.Diagnostics;
using System.Text;

namespace PcSetupMaintainer.Services;

public sealed record ShellResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public sealed class ShellRunner
{
    private readonly AppLogger _logger;

    public ShellRunner(AppLogger logger)
    {
        _logger = logger;
    }

    public async Task<ShellResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default,
        string? workingDirectory = null)
    {
        _logger.Info($"> {fileName} {arguments}");

        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 })
            {
                stdout.AppendLine(e.Data);
                _logger.Info(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 })
            {
                stderr.AppendLine(e.Data);
                _logger.Warn(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        var result = new ShellResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        if (!result.Succeeded)
        {
            _logger.Warn($"{fileName} exited with code {result.ExitCode}");
        }

        return result;
    }

    public async Task<ShellResult> RunPowerShellAsync(string script, CancellationToken cancellationToken = default)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return await RunAsync(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            cancellationToken);
    }
}
