namespace Kora.Setup;

public interface IInstallerLaunchPlatform
{
    bool IsInteractiveNonElevated { get; }
    string ExecutablePath(InstallScope scope, string productVersion);
    Task<string> Sha256Async(string path, CancellationToken cancellationToken);
    void Start(string executable);
}
