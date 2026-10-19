namespace Kora.Windows.Coordination;

public sealed partial class WindowsInstanceCoordinator
{
    private bool evidenceRecoveryReserved;

    public EvidenceCutoverRecoveryAdmissionFailure RecoveryAdmissionFailure { get; private set; } =
        EvidenceCutoverRecoveryAdmissionFailure.OwnershipUnverified;

    public bool EnterEvidenceCutoverRecovery()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ownsMutex || server is not null)
        {
            throw new InvalidOperationException("Evidence recovery cannot enter an active assistant lifetime.");
        }

        try
        {
            if (!Acquire(startupGate, StartupTimeout, allowAbandoned: false))
            {
                RecoveryAdmissionFailure = EvidenceCutoverRecoveryAdmissionFailure.LifecycleBusy;
                FailureReason = "Another lifecycle transition is pending; evidence recovery was not admitted.";
                return false;
            }
            try
            {
                if (!Acquire(owner, TimeSpan.Zero, allowAbandoned: false))
                {
                    RecoveryAdmissionFailure = EvidenceCutoverRecoveryAdmissionFailure.OwnerPresent;
                    FailureReason = "An assistant already owns this profile; offline evidence recovery was not admitted.";
                    return false;
                }
                ownsMutex = true;
                if (!prompt.IsEligible())
                {
                    RecoveryAdmissionFailure = EvidenceCutoverRecoveryAdmissionFailure.SessionUnavailable;
                    FailureReason = "Evidence recovery requires one authoritative unlocked session.";
                    return false;
                }
                if (!prompt.Ask(
                    "Complete only an interrupted first evidence cutover? Existing legacy evidence and the unclean-owner marker will be preserved. " +
                    "No assistant, microphone, provider, task recovery or automatic restart will be started. " +
                    "Any committed inventory, pending publication or candidate segment blocks this operation.",
                    TimeSpan.FromMinutes(2)))
                {
                    RecoveryAdmissionFailure = EvidenceCutoverRecoveryAdmissionFailure.ConfirmationUnavailableOrDeclined;
                    FailureReason = "Evidence recovery requires explicit confirmation in one authoritative unlocked session.";
                    return false;
                }
                // This is an offline maintenance reservation, not a clean assistant lifetime.
                // Never call continuity.Begin, start IPC, or authorize marker release here.
                evidenceRecoveryReserved = true;
                return true;
            }
            finally
            {
                startupGate.ReleaseMutex();
            }
        }
        catch (Exception exception) when (IsLifecycleFailure(exception))
        {
            RecoveryAdmissionFailure = EvidenceCutoverRecoveryAdmissionFailure.OwnershipUnverified;
            FailureReason = $"Evidence recovery ownership could not be verified: {exception.Message}";
            return false;
        }
    }

    public void RequireEvidenceCutoverRecoveryAuthority()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!evidenceRecoveryReserved || !ownsMutex || server is not null || !prompt.IsEligible())
        {
            throw new InvalidOperationException("The explicit evidence-recovery reservation or unlocked session is no longer valid.");
        }
    }
}
