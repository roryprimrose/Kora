using Kora.Core.Platform;
using Kora.Core.Tools;

namespace Kora;

public sealed class DesktopCapabilityHostAccess(
    DesktopInstanceOwnershipBridge ownership,
    ISessionController session,
    IWindowsPrivacyObservationService privacy) : ICapabilityHostAccess
{
    public bool IsCurrentHost => ownership.IsCapabilityAdmissionOpen
        && session.IsCurrentSessionUnlocked()
        && privacy.Current.SessionState == WindowsSessionState.Unlocked;
}
