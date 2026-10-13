using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SavedDraftSummary? catalogueDraft;

    internal async Task<bool> SelectSavedDraftAsync(SavedDraftSummary exact, Func<bool> admitted, CancellationToken token)
    {
        if (!CanRead) { throw new InvalidOperationException("Sessions is busy or private admission is unavailable."); }
        var epoch = selectionEpoch;
        var control = access.ControlRevision;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        bool Current() => !closed && access.CanInspect && access.ControlRevision == control
            && selectionEpoch == epoch && admitted() && !cancellation.IsCancellationRequested;
        using var activity = HostActivity.BeginRoot(new(viewer.RequestId, exact.Session.SessionId, viewer.TaskId, viewer.Origin),
            HostActivityLayer.Desktop, HostOperation.Request);
        busy = true;
        Notify();
        try
        {
            _ = await service.ResolveSavedDraft(exact, Current, cancellation.Token);
            if (!await ProtectComposerAsync()) { activity.Complete(HostOperationOutcome.Cancelled); return false; }
            var target = await service.ResolveSavedDraft(exact, Current, cancellation.Token);
            if (!Current()) { throw new OperationCanceledException("Exact saved draft navigation revoked."); }
            LeaveListSearch();
            ClearSelection();
            sessionFilter = SessionListFilter.All;
            sessions = new([target], null);
            selected = target;
            nameDraft = target.Metadata?.Name.Value ?? string.Empty;
            historySessionId = target.Authority.SessionId.Value.ToString("D");
            catalogueDraft = exact;
            conversationStatus = "Saved draft revision " + exact.DraftRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " observed as metadata only. Explicit Reload reads body data; navigation did not import or submit it.";
            status = "Exact passive saved draft session selected. Done remains Done. No questions retargeted, work resumed or activity renewed.";
            OnPropertyChanged(nameof(SessionFilter));
            activity.Complete(HostOperationOutcome.Completed);
            return true;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        finally { busy = false; Notify(); if (closed) { lifetime.Dispose(); } }
    }

    private async Task VerifyCatalogueReload(Func<bool> live, CancellationToken token)
    {
        if (catalogueDraft is { } exact)
        { _ = await service.ResolveSavedDraft(exact, live, token); }
    }
}
