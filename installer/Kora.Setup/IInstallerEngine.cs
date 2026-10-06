namespace Kora.Setup;

public interface IInstallerEngine
{
    void Plan(SetupAction action, InstallScope scope, bool startAtLogin);

    void Apply();

    void LogFailure(int status);
}
