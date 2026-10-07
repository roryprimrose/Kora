using Kora.Application.ViewModels;
using Kora.Core.Communication;
using Kora.Core.Storage;

namespace Kora;

internal sealed class DesktopSessionWorkspaceAccess(
    DesktopInstanceOwnershipBridge ownership, MainViewModel main) : ISessionWorkspaceAccess
{
    public bool CanInspect => ownership.IsReady && !ownership.IsHandoffRecoveryRequired
        && main.CanRevealPrivatePresentation;
    public bool CanControl => CanInspect && main.CallObservation.EffectiveState is CallState.Clear or CallState.Unavailable;
    public long ControlRevision => main.CallObservation.Revision;
}
