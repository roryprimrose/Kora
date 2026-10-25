using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora;

internal sealed class WorkspaceSurfaceCommandController(
    MainViewModel main, ISessionWorkspaceAccess access, AllWorkWindowController allWork,
    SessionsWindowController sessions) : IDisposable
{
    private bool disposed;

    internal void Bind() => main.WorkspaceSurfaceRequested += Open;

    private async Task Open(WorkspaceSurfaceNavigation navigation)
    {
        var revision = access.ControlRevision;
        var source = sessions.CaptureCompactNavigationEligibility();
        bool Admitted() => !disposed && navigation.IsAdmitted() && main.CanRevealPrivatePresentation
            && access.CanInspect && revision == access.ControlRevision;
        bool SourceAdmitted() => navigation.Target != WorkspaceSurfaceTarget.CompactConversation || source();
        var selection = navigation.Target == WorkspaceSurfaceTarget.SelectSession
            ? sessions.CaptureSessionSelection(navigation.SessionPosition, Admitted) : null;
        if (!Admitted() || !SourceAdmitted())
        {
            main.ReportHostInteractionFailure("Navigation unavailable: original private ownership or selected presentation is no longer admitted.");
            return;
        }
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Presentation);
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            using var activity = continuation();
            if (!Admitted() || !SourceAdmitted())
            {
                main.ReportHostInteractionFailure("Navigation cancelled: private ownership, input or selected presentation changed before native opening.");
                activity.Complete(HostOperationOutcome.Cancelled);
                return;
            }
            switch (navigation.Target)
            {
                case WorkspaceSurfaceTarget.AllWork: allWork.Open(Admitted); break;
                case WorkspaceSurfaceTarget.CompactConversation: sessions.OpenCompact(Admitted); break;
                case WorkspaceSurfaceTarget.Sessions: await sessions.ShowInteractionAsync(true, Admitted); break;
                case WorkspaceSurfaceTarget.SelectSession: await selection!(); break;
                case WorkspaceSurfaceTarget.Details:
                case WorkspaceSurfaceTarget.HideDetails:
                case WorkspaceSurfaceTarget.SessionLogs:
                case WorkspaceSurfaceTarget.SessionAudit:
                    await sessions.ShowDetailsAsync(navigation.Target, Admitted);
                    break;
                default: throw new InvalidOperationException("The native navigation target is unavailable.");
            }
            activity.Complete(HostOperationOutcome.Completed);
        });
    }

    public void Dispose()
    {
        disposed = true;
        main.WorkspaceSurfaceRequested -= Open;
    }
}
