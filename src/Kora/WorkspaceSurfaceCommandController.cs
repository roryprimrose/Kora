using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora;

internal sealed class WorkspaceSurfaceCommandController(
    MainViewModel main, ISessionWorkspaceAccess access, AllWorkWindowController allWork,
    SavedDraftCatalogueWindowController savedDrafts, SessionsWindowController sessions) : IDisposable
{
    private bool disposed;

    internal void Bind()
    {
        main.WorkspaceSurfaceRequested += Open;
        main.SavedDraftCataloguePreparationRequested += PrepareSavedDraftCatalogue;
    }

    private async Task<bool> PrepareSavedDraftCatalogue(SavedDraftCataloguePreparation preparation)
    {
        var revision = access.ControlRevision;
        var lifetime = savedDrafts.CapturePreparationLifetime();
        var source = savedDrafts.CapturePreparationSource();
        bool Admitted() => !disposed && preparation.IsAdmitted() && main.CanRevealPrivatePresentation
            && access.CanInspect && revision == access.ControlRevision && lifetime();
        if (!Admitted()) { return false; }
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Presentation);
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            using var activity = continuation();
            var prepared = Admitted() && savedDrafts.PrepareExactId(preparation.SessionId, Admitted, source) && Admitted();
            activity.Complete(prepared ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
            return prepared;
        });
    }

    private async Task Open(WorkspaceSurfaceNavigation navigation)
    {
        var revision = access.ControlRevision;
        var source = sessions.CaptureCompactNavigationEligibility();
        bool Admitted() => !disposed && navigation.IsAdmitted() && main.CanRevealPrivatePresentation
            && access.CanInspect && revision == access.ControlRevision;
        bool SourceAdmitted() => navigation.Target != WorkspaceSurfaceTarget.CompactConversation || source();
        if (!Admitted() || !SourceAdmitted())
        {
            main.ReportHostInteractionFailure("Navigation unavailable: original private ownership or selected presentation is no longer admitted.");
            return;
        }
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Presentation);
        await Dispatcher.UIThread.InvokeAsync(() =>
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
                case WorkspaceSurfaceTarget.SavedConversationDrafts: savedDrafts.Open(Admitted); break;
                case WorkspaceSurfaceTarget.CompactConversation: sessions.OpenCompact(Admitted); break;
                default: throw new InvalidOperationException("The native navigation target is unavailable.");
            }
            activity.Complete(HostOperationOutcome.Completed);
        });
    }

    public void Dispose()
    {
        disposed = true;
        main.WorkspaceSurfaceRequested -= Open;
        main.SavedDraftCataloguePreparationRequested -= PrepareSavedDraftCatalogue;
    }
}
