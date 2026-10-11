using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using Kora.Application.Infrastructure;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Interaction;

public sealed partial class ExactGrantsViewModel
{
    private ExactGrantInspection? inspectedHistoryTarget;
    private HostRequest? historyViewer;
    private string? historyNext;
    private long historyControlRevision;
    private ImmutableArray<string> historyRows = [];
    private string historySummary = "Explicitly inspect one actual retained record, then read history for that exact grant ID.";

    public AsyncCommand ReadHistoryCommand { get; }
    public AsyncCommand NextHistoryCommand { get; }
    public ImmutableArray<string> HistoryRows { get => historyRows; private set => SetProperty(ref historyRows, value); }
    public string HistorySummary { get => historySummary; private set => SetProperty(ref historySummary, value); }
    public bool CanReadHistory => !disposed && !Busy && inspectedHistoryTarget is not null
        && access.CanInspect && nativeAdmission() && historyControlRevision == access.ControlRevision;

    public Task ReadHistoryAsync(CancellationToken token = default) => ReadHistoryPageAsync(null, token);
    public Task NextHistoryAsync(CancellationToken token = default) => ReadHistoryPageAsync(
        historyNext ?? throw new InvalidOperationException("No exact-ID history continuation is available."), token);

    private async Task ReadHistoryPageAsync(string? cursor, CancellationToken token)
    {
        if (HostActivity.HasScope)
        {
            throw new InvalidOperationException("Exact history requires an explicit native viewer action, not an ambient model or historical request.");
        }
        var exact = inspectedHistoryTarget ?? throw new InvalidOperationException("Inspect one actual retained exact ID before reading history.");
        using var linked = Begin(token);
        var revision = historyControlRevision;
        var version = selectionVersion;
        var filters = filterVersion;
        // Passive navigation retires actionable review, never creates a revoke preview or adopts old request authority.
        preview = null;
        Reviewed = false;
        ClearBatch();
        HistoryRows = [];
        HistorySummary = "Reading the exact retained committed snapshot; no action is being executed.";
        var viewer = cursor is null ? HostRequest.Create(RequestOrigin.LocalUi) : historyViewer!;
        historyViewer = viewer;
        var active = true;
        bool Admitted() => active && !disposed && nativeAdmission() && access.CanInspect && access.ControlRevision == revision
            && selectionVersion == version && ReferenceEquals(inspectedHistoryTarget, exact);
        using var activity = HostActivity.BeginRoot(viewer, HostActivityLayer.Application, HostOperation.Evidence);
        try
        {
            RequireCurrent(revision, version, controlRequired: false, linked.Token);
            var page = await store.ReadExactGrantHistoryAsync(exact, viewer, cursor, Admitted, linked.Token);
            RequireCurrent(revision, version, controlRequired: false, linked.Token);
            if (page.Current is { } current && (current.Grant.Id != exact.Grant.Id
                    || !string.Equals(current.StoreIdentity, exact.StoreIdentity, StringComparison.Ordinal))
                || page.Current is null && (!page.Records.IsEmpty || page.Next is not null)
                || page.Records.Length > ExactGrantHistoryPage.MaximumRecords
                || ExactGrantHistoryPage.Serialize(page).Length > ExactGrantHistoryPage.MaximumBytes)
            {
                throw new InvalidDataException("Exact history returned foreign metadata or exceeded its complete page bounds.");
            }
            inspectedHistoryTarget = page.Current;
            historyNext = page.Next;
            HistoryRows = page.Records.Select(FormatHistory).ToImmutableArray();
            HistorySummary = page.Current is null
                ? "Exact retained record unavailable. No grant or historical rows are fabricated from prior selection."
                : "Exact grant ID: " + exact.Grant.Id.Value.ToString("D")
                    + "; observed: " + page.ObservedAt.ToString("O", CultureInfo.InvariantCulture)
                    + "; finite audit ceiling: " + page.AuditCeiling.ToString(CultureInfo.InvariantCulture)
                    + "; ceiling SHA-256: " + page.AuditCeilingDigest + Environment.NewLine
                    + "Rows in this page: " + page.Records.Length.ToString(CultureInfo.InvariantCulture)
                    + "; scanned rows: " + page.ScannedRows.ToString(CultureInfo.InvariantCulture)
                    + "; scanned through sequence: " + page.ScannedThrough.ToString(CultureInfo.InvariantCulture)
                    + "; scan bound reached: " + page.ScanLimitReached
                    + "; continuation: " + (page.Next is null ? "none in this snapshot" : "more prefix to scan, including zero-match pages")
                    + Environment.NewLine + "Stored use count: " + page.Current.Grant.UseCount.ToString(CultureInfo.InvariantCulture)
                    + "; not reconstructed or compared as exhaustive action history." + Environment.NewLine + ExactGrantHistoryPage.Disclosure;
            Message = page.Current is null ? "Exact record unavailable; refresh current inventory."
                : "Passive exact-ID committed history only. Revocation confirmation cleared; inspect afresh before a new explicit review.";
            OnPropertyChanged(nameof(Inspection));
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && IsRetiredFilter(filters))
        {
            Failed(exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (OperationCanceledException)
        {
            ClearHistory();
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            ClearHistory();
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally { active = false; EndOperation(); }
    }

    private void ClearHistory()
    {
        inspectedHistoryTarget = null;
        historyViewer = null;
        historyNext = null;
        HistoryRows = [];
        HistorySummary = "Explicitly inspect one actual retained record, then read history for that exact grant ID.";
    }

    private static string FormatHistory(ExactGrantHistoryEntry entry)
    {
        var p = entry.Provenance;
        var text = new StringBuilder()
            .AppendLine("Source: " + p.Table + "; schema: " + p.SchemaVersion.ToString(CultureInfo.InvariantCulture)
                + "; sequence: " + p.Sequence.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Committed: " + entry.CommittedAt.ToString("O", CultureInfo.InvariantCulture)
                + "; retention deadline: " + entry.DueAt.ToString("O", CultureInfo.InvariantCulture) + "; " + entry.Retention)
            .AppendLine("Recorded action: " + entry.Audit.ActionId + "; terminal audit: " + entry.Audit.Outcome
                + "; interaction: " + p.InteractionOutcome + "; reason: " + entry.Audit.ReasonCode)
            .AppendLine("Correlation ID: " + entry.Audit.CorrelationId.ToString("D") + "; commit SHA-256: " + p.CommitDigest)
            .AppendLine("Original request/session/task/invocation: " + entry.Request.RequestId.Value.ToString("D") + " / "
                + entry.Request.SessionId.Value.ToString("D") + " / " + entry.Request.TaskId.Value.ToString("D") + " / "
                + entry.Request.InvocationId?.Value.ToString("D"))
            .AppendLine("Origin: " + entry.Request.Origin + " (provenance, not viewer authority); intent revision: "
                + p.IntentRevision.Value.ToString(CultureInfo.InvariantCulture) + "; generation: "
                + p.SessionGeneration.Value.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Original trace/span: " + p.TraceId + " / " + p.SpanId)
            .AppendLine("Original question ID/revision: " + p.QuestionId?.Value.ToString("D") + " / "
                + p.QuestionRevision?.Value.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Requested audit: " + (p.RequestedAudit?.Outcome.ToString() ?? "not separately retained")
                + "; requested exact revision: " + p.RequestedGrantRevision?.Value.ToString(CultureInfo.InvariantCulture));
        foreach (var change in p.RequestedGrants ?? [])
        {
            text.AppendLine("Requested (not committed change): " + change.Id + "; revision: "
                + change.Revision.ToString(CultureInfo.InvariantCulture) + "; SHA-256: " + change.Digest);
        }
        foreach (var change in p.Changes)
        {
            text.AppendLine("Committed exact grant change: " + change.Id + "; revision: "
                + change.Revision.ToString(CultureInfo.InvariantCulture) + "; SHA-256: " + change.Digest);
        }
        return text.Append("Unrelated change references omitted: " + entry.OtherChanges.ToString(CultureInfo.InvariantCulture)
            + "; unrelated requested references omitted: " + entry.OtherRequestedGrants.ToString(CultureInfo.InvariantCulture)
            + ". No execution, cancellation or rollback proof is inferred.").ToString();
    }
}
