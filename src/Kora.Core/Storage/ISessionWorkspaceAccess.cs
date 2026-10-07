namespace Kora.Core.Storage;

public interface ISessionWorkspaceAccess
{
    bool CanInspect { get; }
    bool CanControl { get; }
    long ControlRevision { get; }
}
