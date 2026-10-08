using Kora.Application.ViewModels;
using Kora.Core.Storage;

namespace Kora;

internal sealed class DesktopSessionWorkspaceAccess(
    DesktopInstanceOwnershipBridge ownership, MainViewModel main) : ISessionWorkspaceAccess
{
    public bool CanInspect => ownership.IsReady && !ownership.IsHandoffRecoveryRequired
        && main.CanRevealPrivatePresentation;
    public bool CanControl => CanInspect && !main.CallObservation.IsProtected;
    public long ControlRevision => main.CallObservation.Revision;
}
