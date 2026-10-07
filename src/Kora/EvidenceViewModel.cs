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

    public static IReadOnlyList<EvidenceSource> Sources { get; } = Enum.GetValues<EvidenceSource>();
    public EvidenceSource Source { get; set; }
    public string SafeText { get; set; } = string.Empty;
    public string SessionFilter { get; set; } = string.Empty;
    public string TaskFilter { get; set; } = string.Empty;
    public string TraceFilter { get; set; } = string.Empty;
    public string Status => status;
    public string ResultText => resultText;
    public IReadOnlyList<EvidenceRecord> Records => page?.Records ?? [];
    public IReadOnlyList<EvidenceSegment> Segments => selected?.RelatedSegments ?? [];
    public bool CanQuery => !busy && !closed && canInspect();
    public bool CanNext => CanQuery && page?.Cursor is not null;
    public bool CanReadTrace => CanQuery && selected?.Trace is not null;

    public Task SearchAsync() => RunAsync(() => new()
    {
        Source = Source, Text = string.IsNullOrEmpty(SafeText) ? null : SafeText,
        SessionId = string.IsNullOrWhiteSpace(SessionFilter) ? null : new(Guid.ParseExact(SessionFilter, "D")),
        TaskId = string.IsNullOrWhiteSpace(TaskFilter) ? null : new(Guid.ParseExact(TaskFilter, "D")),
        TraceId = string.IsNullOrEmpty(TraceFilter) ? null : TraceFilter,
    }, next: false);

    public Task NextAsync() => RunAsync(() => currentQuery
        ?? throw new InvalidOperationException("Search before requesting a next page."), next: true);

    public Task ReadTraceAsync() => RunAsync(() => new()
    {
        TraceId = selected?.Trace?.TraceId
            ?? throw new InvalidOperationException("Select a record with trace correlation."),
        SessionId = currentQuery?.SessionId,
    }, next: false);

    public Task NavigateAsync(EvidenceSegment segment) => RunAsync(() => new()
    {
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
        busy = true;
        Notify();
        try
        {
            if (!canInspect()) { throw new InvalidOperationException("Evidence inspection is denied by host ownership or privacy."); }
            var target = create();
            var cursor = next ? page?.Cursor ?? throw new InvalidOperationException("No continuation page is available.") : null;
            var result = await query.QueryAsync(target, cursor, lifetime.Token);
            if (closed || !canInspect()) { throw new OperationCanceledException("Evidence presentation closed."); }
            currentQuery = target;
            page = result;
            selected = null;
            resultText = Encoding.UTF8.GetString(DurableEvidenceQuery.Serialize(result));
            status = $"{result.Status}: {result.Records.Count} records. "
                + (result.Cursor is not null ? "More bounded pages available. " : "End of this filtered snapshot. ")
                + EvidencePage.StorageDisclosure;
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            Clear("Evidence query cancelled or privacy closed. No late content was presented.");
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Clear("Evidence query failed: " + exception.GetType().Name
                + ". Verify filter format and private storage access, then start a fresh search. No empty success or replacement database is claimed.");
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            busy = false;
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
