using Kora.Application.Diagnostics;
using Kora.Core.Platform;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private long privatePresentationRevision;
    private int privatePresentationRetired = 1;

    private bool IsPrivatePresentationRecoveryEligible => !disposed && !lifecycleAdmissionClosed
        && !auditEvidenceUnavailable && sessionController.IsCurrentSessionUnlocked()
        && privacyObservation.Current.SessionState == WindowsSessionState.Unlocked && callOwnershipEligible();

    private long BeginPrivatePresentationRetirement()
    {
        var revision = Interlocked.Increment(ref privatePresentationRevision);
        Volatile.Write(ref privatePresentationRetired, 0);
        Interlocked.Exchange(ref privacyPresentationHeld, 1);
        return revision;
    }

    private void CompletePrivatePresentationRetirement(long revision)
    {
        if (revision == Interlocked.Read(ref privatePresentationRevision))
        {
            Volatile.Write(ref privatePresentationRetired, 1);
        }
    }

    private bool TryRecoverPrivatePresentation()
    {
        if (IsPrivacyPresentationHeld && OriginalOrigin() == RequestOrigin.HostSystem)
        {
            ApplicationLog.Information(logger, "Denied private presentation recovery without an explicit original user gesture");
            return false;
        }
        if (!IsPrivatePresentationRecoveryEligible)
        {
            ApplicationLog.Information(logger, "Denied private presentation recovery outside current desktop ownership and privacy admission");
            return false;
        }
        if (IsPrivacyPresentationHeld && (Volatile.Read(ref privatePresentationRetired) == 0
            || !voiceRecognition.IsCaptureQuiescent || textToSpeech.IsSpeaking))
        {
            ApplicationLog.Information(logger, "Denied private presentation recovery before successful joined retirement");
            return false;
        }
        Interlocked.Exchange(ref privacyPresentationHeld, 0);
        OnPropertyChanged(nameof(IsPrivacyPresentationHeld));
        if (IsPrivacyPresentationHeld || !IsPrivatePresentationRecoveryEligible)
        {
            Interlocked.Exchange(ref privacyPresentationHeld, 1);
            ApplicationLog.Information(logger, "Denied private presentation recovery after reentrant desktop admission loss");
            return false;
        }
        return true;
    }
}
