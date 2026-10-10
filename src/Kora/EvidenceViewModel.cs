using System.Text;

using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class EvidenceViewModel(
    DurableEvidenceQuery query, Func<bool> canInspect, ILogger<EvidenceViewModel> logger) : ObservableObject
{
    private readonly HostRequest viewer = HostRequest.Create(RequestOrigin.LocalUi);
    private readonly CancellationTokenSource lifetime = new();
    private EvidencePage? page;
    private EvidenceQuery? currentQuery;
    private EvidenceRecord? selected;
    private string status = EvidencePage.StorageDisclosure;
    private string resultText = string.Empty;
    private bool busy;
    private bool closed;
    private int filterRevision;
    private CancellationTokenSource? readCancellation;

    public static IReadOnlyList<EvidenceSource> Sources { get; } = Enum.GetValues<EvidenceSource>();
    public string Status => status;
    public string ResultText => resultText;
    public IReadOnlyList<EvidenceRecord> Records => page?.Records ?? [];
    public IReadOnlyList<EvidenceSegment> Segments => selected?.RelatedSegments ?? [];
    public bool CanQuery => !busy && !closed && canInspect();
    public bool CanNext => CanQuery && page?.Cursor is not null;
    public bool CanReadTrace => CanQuery && (selected?.Trace is not null || selected?.AuthorityProvenance is not null);

    public Task SearchAsync() => RunAsync(CreateQuery, next: false);

    public Task NextAsync() => RunAsync(() => currentQuery
        ?? throw new InvalidOperationException("Search before requesting a next page."), next: true);

    public Task ReadTraceAsync() => RunAsync(() => new()
    {
        Source = currentQuery?.Source == EvidenceSource.CombinedLog ? EvidenceSource.CombinedLog
            : selected?.Reference.Source == EvidenceSource.AuthorityAudit ? EvidenceSource.AuthorityAudit
            : selected?.Reference.Source == EvidenceSource.DailyLog ? EvidenceSource.DailyLog : EvidenceSource.All,
        TraceId = selected?.Trace?.TraceId ?? selected?.AuthorityProvenance?.TraceId
            ?? throw new InvalidOperationException("Select a record with trace correlation."),
        SessionId = currentQuery?.SessionId,
    }, next: false);

    public Task NavigateAsync(EvidenceSegment segment) => RunAsync(() => new()
    {
        Source = segment.Record?.Source == EvidenceSource.AuthorityAudit ? EvidenceSource.AuthorityAudit : EvidenceSource.All,
        Record = segment.Record ?? throw new InvalidOperationException("This segment is missing or removed; no retained record can be opened."),
        SessionId = currentQuery?.SessionId,
    }, next: false);

    public void Select(EvidenceRecord? record)
    {
        if (closed || !canInspect()) { selected = null; Notify(); return; }
        if (record is not null && !Records.Contains(record))
        {
            throw new InvalidOperationException("Select only an admitted record from the current page.");
        }
        selected = record;
        Notify();
    }

    private async Task RunAsync(Func<EvidenceQuery> create, bool next)
    {
        if (busy || closed) { return; }
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), viewer.SessionId,
            viewer.TaskId, RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        readCancellation = cancellation;
        var revision = filterRevision;
        busy = true;
        Notify();
        try
        {
            if (!canInspect()) { throw new InvalidOperationException("Evidence inspection is denied by host ownership or privacy."); }
            var target = create();
            var cursor = next ? page?.Cursor ?? throw new InvalidOperationException("No continuation page is available.") : null;
            Clear("Reading a bounded evidence snapshot.");
            Notify();
            var result = await query.QueryAsync(target, cursor, cancellation.Token);
            if (closed || revision != filterRevision || !canInspect()) { throw new OperationCanceledException("Evidence presentation retired."); }
            currentQuery = target;
            page = result;
            selected = null;
            resultText = Encoding.UTF8.GetString(DurableEvidenceQuery.Serialize(result));
            var sourceFailed = target.Source is EvidenceSource.CombinedLog or EvidenceSource.AuthorityAudit
                && result.UnavailableSources.Count > 0;
            status = $"{result.Status}: {result.Records.Count} records. "
                + (sourceFailed
                    ? "An included source could not be read; no complete or empty-success result is claimed. Verify access and start a fresh search. "
                    : result.Cursor is not null ? "More bounded pages available. " : "End of this filtered snapshot. ")
                + result.Disclosure;
            activity.Complete(sourceFailed ? HostOperationOutcome.Failed : HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            if (closed || revision == filterRevision)
            {
                Clear("Evidence query cancelled or privacy closed. No late content was presented.");
            }
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (NotSupportedException)
        {
            Clear("Unsupported filter for this source. Severity requires All, Log, Audit, DailyLog or CombinedLog; audit outcome requires All, Audit or AuthorityAudit. Clear the unsupported filter or select its source, then search again.");
            Failure(logger, nameof(NotSupportedException));
            activity.Complete(HostOperationOutcome.Failed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (closed || revision == filterRevision)
            {
                Clear("Evidence query failed: " + exception.GetType().Name
                    + ". Use nonempty hyphenated GUIDs and ISO timestamps with Z or an explicit offset; verify range, typed choices and private storage access, then start a fresh search. No empty success or replacement database is claimed.");
            }
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            busy = false;
            readCancellation = null;
            if (closed) { lifetime.Dispose(); }
            Notify();
        }
    }

    public void Close()
    {
        if (closed) { return; }
        closed = true;
        lifetime.Cancel();
        if (!busy) { lifetime.Dispose(); }
        Clear("Evidence viewer closed; retained sources were not changed.");
        SafeText = string.Empty;
        SessionFilter = string.Empty;
        TaskFilter = string.Empty;
        TraceFilter = string.Empty;
        ClearAdvancedFilters();
        Notify();
    }

    private void Clear(string message)
    {
        page = null;
        selected = null;
        currentQuery = null;
        resultText = string.Empty;
        status = message;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(Records));
        OnPropertyChanged(nameof(Segments));
        OnPropertyChanged(nameof(CanQuery));
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanReadTrace));
    }
}
