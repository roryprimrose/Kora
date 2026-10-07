using Kora.Application.ViewModels;
using Kora.Core.Diagnostics;

namespace Kora;

internal sealed class DesktopEvidenceAccess(
    DesktopInstanceOwnershipBridge ownership, MainViewModel main) : IEvidenceQueryAccess
{
    public bool CanInspect => ownership.IsReady && !ownership.IsHandoffRecoveryRequired
        && main.CanRevealPrivatePresentation;
}
