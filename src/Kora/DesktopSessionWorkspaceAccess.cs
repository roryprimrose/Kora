using Kora.Application.ViewModels;
using Kora.Core.Storage;

namespace Kora;

internal sealed class DesktopSessionWorkspaceAccess : ISessionWorkspaceAccess, IDisposable
{
    private readonly DesktopInstanceOwnershipBridge ownership;
    private readonly MainViewModel main;
    private readonly Lock gate = new();
    private long revision;
    private long privacyEpoch;
    private (long Call, long Owner, long Privacy) observed;
    private volatile bool disposed;

    public DesktopSessionWorkspaceAccess(DesktopInstanceOwnershipBridge ownership, MainViewModel main)
    {
        this.ownership = ownership;
        this.main = main;
        main.PrivacyClosureRequested += OnPrivacyClosure;
    }

    public bool CanInspect => !disposed && ownership.IsCapabilityAdmissionOpen && !ownership.IsHandoffRecoveryRequired
        && main.CanRevealPrivatePresentation;
    public bool CanControl => CanInspect && !main.CallObservation.IsProtected;
    public long ControlRevision
    {
        get
        {
            lock (gate)
            {
                var current = (main.CallObservation.Revision, ownership.AdmissionRevision, privacyEpoch);
                if (current != observed) { revision = checked(revision + 1); observed = current; }
                return revision;
            }
        }
    }

    private void OnPrivacyClosure(object? sender, EventArgs args)
    {
        lock (gate) { privacyEpoch = checked(privacyEpoch + 1); }
    }

    public void Dispose()
    {
        disposed = true;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
    }
}
