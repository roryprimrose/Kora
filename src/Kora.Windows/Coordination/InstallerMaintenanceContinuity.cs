using System.Runtime.ExceptionServices;

namespace Kora.Windows.Coordination;

internal sealed class InstallerMaintenanceContinuity : IDisposable
{
    private readonly Lock sync = new();
    private readonly IInstallerMaintenanceMarker ownerMarker;
    private readonly IInstallerMaintenanceMarker debugGuard;
    private bool applyStarted;
    private bool applyComplete;
    private bool sealedForDisposal;
    private bool clearOnDisposal;
    private bool disposed;
    private ExceptionDispatchInfo? disposalFailure;

    internal InstallerMaintenanceContinuity(IInstallerMaintenanceMarkerStore store)
    {
        // Debug recovery must already be fenced before the Release-visible marker can appear.
        debugGuard = store.CreateDurable(InstallerContinuityMarkerKind.DebugRecoveryGuard);
        try { ownerMarker = store.CreateDurable(InstallerContinuityMarkerKind.Owner); }
        catch
        {
            debugGuard.Dispose();
            throw;
        }
    }

    internal void BeginNativeApply()
    {
        lock (sync)
        {
            if (sealedForDisposal || applyStarted)
            {
                throw new InvalidOperationException("Native Apply requires one active maintenance reservation.");
            }
            applyStarted = true;
        }
    }

    internal bool ConfirmNativeApplyComplete()
    {
        lock (sync)
        {
            if (sealedForDisposal || !applyStarted) { return false; }
            applyComplete = true;
            return true;
        }
    }

    internal void SealForDisposal()
    {
        lock (sync)
        {
            if (sealedForDisposal) { return; }
            clearOnDisposal = !applyStarted || applyComplete;
            sealedForDisposal = true;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                disposalFailure?.Throw();
                return;
            }
            SealForDisposal();
            disposed = true;
            Exception? failure = null;
            try
            {
                if (clearOnDisposal)
                {
                    ownerMarker.DeleteOwned();
                    debugGuard.DeleteOwned();
                }
            }
            catch (Exception exception) { failure = exception; }
            try { ownerMarker.Dispose(); }
            catch (Exception exception) { failure ??= exception; }
            try { debugGuard.Dispose(); }
            catch (Exception exception) { failure ??= exception; }
            if (failure is not null)
            {
                disposalFailure = ExceptionDispatchInfo.Capture(failure);
                disposalFailure.Throw();
            }
        }
    }
}
