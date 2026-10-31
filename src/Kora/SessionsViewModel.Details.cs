using System.Text.Json;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private enum DetailPart { Original, Assistant, History, Evidence, Observation }
    private sealed record DetailSelection(DetailPart Part, Guid Item, Guid? Observation);
    private readonly Dictionary<HostId<SessionIdentity>, DetailSelection> detailSelections = [];
    private DetailSelection? CurrentDetailSelection => selected is { } entry
        ? detailSelections.GetValueOrDefault(entry.Authority.SessionId) : null;
    internal bool LastDetailAssistant => CurrentDetailSelection?.Part == DetailPart.Assistant;
    internal Guid? LastDetailObservation => CurrentDetailSelection?.Observation;
    internal ConversationMessage? LastDetailMessage => CurrentDetailSelection is { Part: DetailPart.Original or DetailPart.Assistant or DetailPart.Observation } choice
        ? ConversationMessages.FirstOrDefault(message => message.Request.RequestId.Value == choice.Item) : null;
    internal bool CanRestoreHistoryDetail => CanOpenHistoryDetail && CurrentDetailSelection is { Part: DetailPart.History } choice
        && selectedHistory?.Id == choice.Item;
    internal SessionEvidenceDetail? LastEvidenceDetail => CurrentDetailSelection is { Part: DetailPart.Evidence } choice
        ? evidenceDetails.FirstOrDefault(snapshot => snapshot.Id == choice.Item) : null;

    private void RememberSelectedDetail(DetailPart part, Guid item, Guid? observation = null)
    {
        var subject = RequireSelected().Authority.SessionId;
        if (!detailSelections.ContainsKey(subject) && detailSelections.Count == 8) { detailSelections.Remove(detailSelections.Keys.First()); }
        detailSelections[subject] = new(part, item, observation);
    }

    internal void ReportDetailPresentationFailure(Exception exception)
    {
        Failure(logger, exception.GetType().Name);
        status = "Details presentation/revalidation failed. Unconfirmed content was cleared; select the exact source and retry.";
        Notify();
    }

    internal Func<SessionEvidenceDetail, Func<bool>, Func<CancellationToken, Task<bool>>, string>? PresentEvidenceDetails { get; set; }
    private readonly List<SessionEvidenceDetail> evidenceDetails = [];
    public IReadOnlyList<SessionEvidenceDetail> EvidenceDetails => CanPresentDetails ? evidenceDetails.ToArray() : [];
    public bool HasEvidenceDetails => CanPresentDetails && selected is not null && evidenceDetails.Count != 0;

    public Task ReadEvidenceDetailsAsync(EvidenceSource source) => RunAsync(async () =>
    {
        if (source is not (EvidenceSource.Log or EvidenceSource.AuthorityAudit))
        { throw new InvalidOperationException("Select Logs or typed AuthorityAudit; scope cannot be broadened by the pane."); }
        var target = RequireSelected();
        var epoch = selectionEpoch;
        var control = access.ControlRevision;
        var page = await evidence.QueryAsync(new() { SessionId = target.Authority.SessionId, Source = source }, null, lifetime.Token);
        if (closed || !access.CanInspect || access.ControlRevision != control
            || selectionEpoch != epoch || selected != target)
        { throw new OperationCanceledException("The exact session evidence request was retired."); }
        var snapshot = new SessionEvidenceDetail(source, page);
        if (evidenceDetails.Count == 8) { evidenceDetails.RemoveAt(0); }
        evidenceDetails.Add(snapshot);
        status = PresentEvidencePage(snapshot, target, epoch, control);
    });

    public Task OpenEvidenceDetailsAsync(SessionEvidenceDetail? detail = null) => RunAsync(async () =>
    {
        var snapshot = detail ?? LastEvidenceDetail
            ?? throw new InvalidOperationException("Choose an exact session evidence result; no latest-result substitution is allowed.");
        var target = RequireSelected();
        var epoch = selectionEpoch;
        var control = access.ControlRevision;
        bool Current() => EvidenceDetailCurrent(snapshot, target, epoch, control);
        if (!Current()) { throw new InvalidOperationException("This evidence result is no longer on the selected session's observed page."); }
        if (!await ValidateEvidencePageAsync(snapshot, Current, lifetime.Token))
        { throw new InvalidOperationException("The original evidence source changed or is unavailable. Explicitly read fresh session evidence."); }
        status = PresentEvidencePage(snapshot, target, epoch, control);
    });

    private bool EvidenceDetailCurrent(SessionEvidenceDetail detail, Kora.Core.Storage.SessionWorkspaceEntry target, long epoch, long control) =>
        !closed && access.CanInspect && access.ControlRevision == control && selectionEpoch == epoch
        && selected == target && evidenceDetails.Any(item => ReferenceEquals(item, detail));

    private string PresentEvidencePage(SessionEvidenceDetail detail, Kora.Core.Storage.SessionWorkspaceEntry target, long epoch, long control)
    {
        bool Current() => EvidenceDetailCurrent(detail, target, epoch, control);
        var continueValidation = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Storage);
        async Task<bool> Validate(CancellationToken token)
        {
            using var activity = continueValidation();
            try
            {
                var valid = await ValidateEvidencePageAsync(detail, Current, token);
                activity.Complete(valid ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
                if (!valid && Current())
                {
                    evidenceDetails.Remove(detail);
                    status = "Session evidence Details retired: the original retained source changed or is unavailable. Read fresh evidence explicitly.";
                    Notify();
                }
                return valid;
            }
            catch (OperationCanceledException)
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                return false;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Failure(logger, exception.GetType().Name);
                activity.Complete(HostOperationOutcome.Failed);
                if (Current())
                {
                    evidenceDetails.Remove(detail);
                    status = "Session evidence Details revalidation failed. The old content was cleared; explicitly read evidence again.";
                    Notify();
                }
                return false;
            }
        }
        RememberSelectedDetail(DetailPart.Evidence, detail.Id);
        return (PresentEvidenceDetails ?? throw new InvalidOperationException("Session evidence Details is not composed."))(
            detail, Current, Validate);
    }

    private async Task<bool> ValidateEvidencePageAsync(SessionEvidenceDetail detail, Func<bool> current, CancellationToken token)
    {
        if (!current()) { return false; }
        if (detail.Page.Records.Count == 0)
        {
            var source = await evidence.QueryAsync(new()
            { Source = detail.Source, SessionId = RequireSelected().Authority.SessionId, Limit = 1 }, null, token);
            return current() && source.Status != EvidencePageStatus.Unavailable && source.UnavailableSources.Count == 0;
        }
        foreach (var original in detail.Page.Records)
        {
            var page = await evidence.QueryAsync(new()
            { Source = detail.Source, SessionId = RequireSelected().Authority.SessionId, Record = original.Reference, Limit = 1 }, null, token);
            if (!current() || page.UnavailableSources.Count != 0 || page.Records.Count != 1
                || !JsonSerializer.SerializeToUtf8Bytes(original with { ObservedUtc = null }).AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(page.Records[0] with { ObservedUtc = null })))
            { return false; }
        }
        return current();
    }

    private void ClearEvidenceDetails()
    {
        evidenceDetails.Clear();
    }
}
