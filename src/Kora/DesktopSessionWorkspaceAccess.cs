using Kora.Application.ViewModels;
using Kora.Core.Storage;

namespace Kora;

internal sealed class DesktopSessionWorkspaceAccess : ISessionWorkspaceAccess, IDisposable
{
    private readonly DesktopInstanceOwnershipBridge ownership;
    private MainViewModel? main;
    private readonly Lock gate = new();
    private long revision;
    private long privacyEpoch;
    private (long Call, long Owner, long Privacy) observed;
    private volatile bool disposed;

    public DesktopSessionWorkspaceAccess(DesktopInstanceOwnershipBridge ownership)
    {
        this.ownership = ownership;
    }

    public void Bind(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (main is not null)
            {
                throw new InvalidOperationException("Workspace access is already bound to its desktop view model.");
            }
            revision = checked(revision + 1);
            main = viewModel;
            main.PrivacyClosureRequested += OnPrivacyClosure;
        }
    }

    public bool CanInspect
    {
        get
        {
            lock (gate)
            {
                return !disposed && main is not null && ownership.IsCapabilityAdmissionOpen
                    && !ownership.IsHandoffRecoveryRequired && main.CanRevealPrivatePresentation;
            }
        }
    }

    public bool CanControl
    {
        get
        {
            lock (gate)
            {
                return CanInspect && main is not null && !main.CallObservation.IsProtected;
            }
        }
    }
    public long ControlRevision
    {
        get
        {
            lock (gate)
            {
                var current = (main?.CallObservation.Revision ?? 0, ownership.AdmissionRevision, privacyEpoch);
                if (current != observed) { revision = checked(revision + 1); observed = current; }
                return revision;
            }
        }
    }

    private void OnPrivacyClosure(object? sender, EventArgs args)
    {
        lock (gate)
        {
            if (!disposed) { privacyEpoch = checked(privacyEpoch + 1); }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
            revision = checked(revision + 1);
            if (main is not null)
            {
                main.PrivacyClosureRequested -= OnPrivacyClosure;
                main = null;
            }
        }
    }
}
