using System.Diagnostics;

namespace Kora.Windows.Dependencies;

internal sealed class OllamaProcessRunner : IOllamaProcessRunner
{
    private readonly Lock gate = new();
    private Process? ownedServer;
#if DEBUG
    private readonly Kora.Windows.Coordination.IDebugRecoveryWorkTracker? recovery;
    private Kora.Windows.Coordination.IDebugRecoveryWork? serverRecovery;

    internal OllamaProcessRunner(Kora.Windows.Coordination.IDebugRecoveryWorkTracker? recovery = null)
    {
        this.recovery = recovery;
    }

    public Kora.Windows.Coordination.IDebugRecoveryWork BeginWork() =>
        (recovery ?? throw new InvalidOperationException("Debug external work requires host recovery tracking.")).BeginWork();
#endif

    public bool HasInstalledRuntime(string executable) => File.Exists(executable);

    public async Task<int> InstallWingetAsync(CancellationToken cancellationToken)
    {
#if DEBUG
        cancellationToken.ThrowIfCancellationRequested();
        using var work = BeginWork();
#endif
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

#if DEBUG
        work.Complete();
#endif
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
#if DEBUG
            serverRecovery?.Complete();
            serverRecovery?.Dispose();
            serverRecovery = null;
#endif

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "serve" },
            };
            startInfo.Environment["OLLAMA_HOST"] = "127.0.0.1:11434";
#if DEBUG
            var work = BeginWork();
#endif
            var server = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Ollama could not be started.");
            ownedServer?.Dispose();
            ownedServer = server;
#if DEBUG
            serverRecovery = work;
#endif
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

#if DEBUG
                if (!ownedServer.WaitForExit(5000))
                {
                    throw new TimeoutException("The owned Ollama process did not stop; Debug recovery remains blocked.");
                }
                serverRecovery?.Complete();
                serverRecovery?.Dispose();
                serverRecovery = null;
#endif
                ownedServer.Dispose();
                ownedServer = null;
            }
        }
    }
}
