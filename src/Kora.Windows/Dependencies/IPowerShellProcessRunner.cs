namespace Kora.Windows.Dependencies;

internal interface IPowerShellProcessRunner
{
    IReadOnlyList<string> GetCandidatePaths();

    Task<string> GetVersionAsync(string executable, CancellationToken cancellationToken);

    Task<int> InstallAsync(CancellationToken cancellationToken);
}
