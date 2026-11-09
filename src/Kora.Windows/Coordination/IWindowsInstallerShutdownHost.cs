namespace Kora.Windows.Coordination;

public interface IWindowsInstallerShutdownHost
{
    void BindInstallerShutdown(Func<CancellationToken, Task<bool>>? shutdown);
}
