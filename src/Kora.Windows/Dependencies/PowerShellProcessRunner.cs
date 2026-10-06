using System.Diagnostics;

namespace Kora.Windows.Dependencies;

internal sealed class PowerShellProcessRunner : IPowerShellProcessRunner
{
    public IReadOnlyList<string> GetCandidatePaths() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PowerShell", "7", "pwsh.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "PowerShell", "7", "pwsh.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "pwsh.exe"),
    ];

    public async Task<string> GetVersionAsync(string executable, CancellationToken cancellationToken)
    {
        var start = CreateVersionStartInfo(executable);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("PowerShell could not be started for verification.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var version = await output;
            var failure = await error;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"PowerShell verification exited with code {process.ExitCode}: {failure.Trim()}");
            }
            return version.Trim();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("PowerShell version verification timed out.", exception);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    public async Task<int> InstallAsync(CancellationToken cancellationToken)
    {
        var start = CreateInstallationStartInfo();
        if (!File.Exists(start.FileName))
        {
            throw new FileNotFoundException("Windows Package Manager (winget) is required to install PowerShell 7.",
                start.FileName);
        }
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("winget could not be started.");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }
    }

    internal static ProcessStartInfo CreateVersionStartInfo(string executable)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                "$PSVersionTable.PSVersion.ToString()" },
        };
        start.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
        start.Environment["POWERSHELL_UPDATECHECK"] = "Off";
        return start;
    }

    internal static ProcessStartInfo CreateInstallationStartInfo() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "install", "--id", "Microsoft.PowerShell", "--exact",
                "--scope", "user", "--source", "winget",
                "--accept-package-agreements", "--accept-source-agreements",
                "--disable-interactivity",
            },
        };
}
