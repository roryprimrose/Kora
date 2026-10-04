using System.Diagnostics;

namespace Kora.Windows.Dependencies;

internal sealed class OllamaProcessRunner : IOllamaProcessRunner
{
    private readonly Lock gate = new();
    private Process? ownedServer;

    public bool HasInstalledRuntime(string executable) => File.Exists(executable);

    public async Task<int> InstallWingetAsync(CancellationToken cancellationToken)
    {
        using var process = Process.Start(CreateInstallationStartInfo())
            ?? throw new InvalidOperationException("winget could not be started.");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        return process.ExitCode;
    }

    internal static ProcessStartInfo CreateInstallationStartInfo() =>
        new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "install", "--id", WindowsOllamaSetupService.PackageId, "--exact",
                "--version", WindowsOllamaSetupService.PackageVersion,
                "--scope", "user", "--source", "winget",
                "--accept-package-agreements", "--accept-source-agreements",
                "--disable-interactivity",
            },
        };

    public void StartServer(string executable)
    {
        lock (gate)
        {
            if (ownedServer is { HasExited: false })
            {
                throw new InvalidOperationException("Kora already started an Ollama runtime.");
            }

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "serve" },
            };
            startInfo.Environment["OLLAMA_HOST"] = "127.0.0.1:11434";
            var server = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Ollama could not be started.");
            ownedServer?.Dispose();
            ownedServer = server;
        }
    }

    public void StopOwnedServer()
    {
        lock (gate)
        {
            if (ownedServer is not null)
            {
                if (!ownedServer.HasExited)
                {
                    ownedServer.Kill(entireProcessTree: true);
                }

                ownedServer.Dispose();
                ownedServer = null;
            }
        }
    }
}
