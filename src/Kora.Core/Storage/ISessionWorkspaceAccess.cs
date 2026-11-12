namespace Kora.Core.Storage;

public interface ISessionWorkspaceAccess
{
    bool CanInspect { get; }
    bool CanControl { get; }
    // An original native Exit attempt may prove empty work without granting private inspection or control.
    bool CanReviewApplicationExit => false;
    long ControlRevision { get; }
}
