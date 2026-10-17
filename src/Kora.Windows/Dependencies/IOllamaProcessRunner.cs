namespace Kora.Windows.Dependencies;

internal interface IOllamaProcessRunner
#if DEBUG
    : Kora.Windows.Coordination.IDebugRecoveryWorkTracker
#endif
{
    bool HasInstalledRuntime(string executable);

    Task<int> InstallWingetAsync(CancellationToken cancellationToken);

    void StartServer(string executable);

    void StopOwnedServer();
}
