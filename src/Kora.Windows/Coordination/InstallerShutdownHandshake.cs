namespace Kora.Windows.Coordination;

internal static class InstallerShutdownHandshake
{
    internal static Task RequestStopAsync(Stream pipe, Func<CancellationToken, Task> waitForExit,
        CancellationToken cancellationToken) =>
        InstanceShutdownHandshake.RequestStopAsync(pipe, waitForExit, "InstallerStop", cancellationToken);
}
