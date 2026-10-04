namespace Kora.Windows.Dependencies;

internal interface IOllamaProcessRunner
{
    bool HasInstalledRuntime(string executable);

    Task<int> InstallWingetAsync(CancellationToken cancellationToken);

    void StartServer(string executable);

    void StopOwnedServer();
}
