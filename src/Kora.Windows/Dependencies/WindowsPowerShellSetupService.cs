using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

public sealed class WindowsPowerShellSetupService : IPowerShellSetup
{
    private static readonly Version MinimumVersion = new(7, 4);
    private readonly IPowerShellProcessRunner processes;

    public WindowsPowerShellSetupService()
        : this(new PowerShellProcessRunner())
    {
    }

    internal WindowsPowerShellSetupService(IPowerShellProcessRunner processes)
    {
        this.processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    public string TaskId => "powershell.runtime";

    public string TaskName => "PowerShell 7 (pwsh)";

    public async ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        DependencyStatus? problem = null;
        foreach (var executable in processes.GetCandidatePaths())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(executable))
            {
                continue;
            }

            try
            {
                var output = await processes.GetVersionAsync(executable, cancellationToken);
                if (!Version.TryParse(output, out var version)
                    || version.Major != 7
                    || version < MinimumVersion)
                {
                    problem ??= Status(DependencyReadiness.Incompatible,
                        $"The installed PowerShell at {executable} reported an unsupported version ({output}). PowerShell 7.4 or later is required.");
                    continue;
                }

                return Status(DependencyReadiness.Ready,
                    $"PowerShell {version} passed a no-profile execution check at {executable}.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or TimeoutException or System.ComponentModel.Win32Exception
                or IOException or UnauthorizedAccessException)
            {
                problem ??= Status(DependencyReadiness.Failed,
                    $"PowerShell at {executable} could not be verified: {exception.Message}");
            }
        }

        return problem ?? Status(DependencyReadiness.Missing,
            "PowerShell 7 (pwsh.exe) was not found in a supported installation location. Review installation before Kora runs script-backed tasks.");
    }

    public async Task InstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((await ProbeAsync(cancellationToken)).Readiness == DependencyReadiness.Ready)
        {
            return;
        }

        var exitCode = await processes.InstallAsync(cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"winget PowerShell installation failed with exit code {exitCode}.");
        }

        var verified = await ProbeAsync(cancellationToken);
        if (verified.Readiness != DependencyReadiness.Ready)
        {
            throw new InvalidOperationException($"PowerShell installation did not pass verification: {verified.Detail}");
        }
    }

    private DependencyStatus Status(DependencyReadiness readiness, string detail) =>
        new(TaskId, TaskName, readiness, detail);
}
