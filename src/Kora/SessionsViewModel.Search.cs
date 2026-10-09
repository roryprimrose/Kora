using System.Globalization;
using System.Text;

using Kora.Core.Commands;
using Kora.Core.Context;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private string historyQuery = string.Empty;
    private SessionHistorySearchPage? historySearch;
    private CancellationTokenSource? historySearchCancellation;

    public string HistoryQuery
    {
        get => historyQuery;
        set
        {
            historySearchCancellation?.Cancel();
            historyQuery = value;
            historySearch = null;
            history = null;
            selectedHistory = null;
            detail = string.Empty;
            selectionEpoch++;
            status = LocalFileRetrievalPolicy.TryQuery(value, out _)
                ? "Query changed; start a fresh exact-session lexical search."
                : "Enter 1-256 characters / 512 UTF-8 bytes containing 1-32 distinct whole words of at most 64 characters. Query/results are volatile.";
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(Detail));
            NotifyHistory();
        }
    }

    public bool CanSearchHistory => CanHistory && !refreshingWork
        && LocalFileRetrievalPolicy.TryQuery(historyQuery, out _);
    public bool CanNextHistorySearch => CanSearchHistory && historySearch?.Next is not null;
    public bool CanCancelHistorySearch => !closed && historySearchCancellation is not null;

    public void CancelHistorySearch() => historySearchCancellation?.Cancel();

    public Task SearchHistoryAsync(bool next = false) => RunAsync(async () =>
    {
        var epoch = selectionEpoch;
        var subjectText = historySessionId;
        if (!Guid.TryParseExact(subjectText, "D", out var id) || id == Guid.Empty)
        {
            throw new InvalidOperationException("Enter the exact immutable session ID, not a name.");
        }
        var continuation = next ? historySearch?.Next
            ?? throw new InvalidOperationException("No next search snapshot page; start a fresh search.") : null;
        var query = historyQuery;
        history = null;
        selectedHistory = null;
        detail = string.Empty;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        historySearchCancellation = cancellation;
        NotifyHistory();
        try
        {
            var result = await service.SearchHistoryAsync(new(id), query, continuation, 25, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (closed || selectionEpoch != epoch || !string.Equals(historySessionId, subjectText, StringComparison.Ordinal)
                || !string.Equals(historyQuery, query, StringComparison.Ordinal))
            {
                throw new OperationCanceledException("The exact history subject or query changed; start a fresh search.");
            }
            historySearch = result;
            history = new(result.SessionId, result.Generation, result.Disposed, result.Snapshot, result.Records, null);
            detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(new("observed", SessionHistorySearchPage.Scope)
                { HistorySearch = result }));
            status = "Passive lexical search: " + result.Records.Length.ToString(CultureInfo.InvariantCulture)
                + " matches, " + result.Scanned.ToString(CultureInfo.InvariantCulture) + " scanned, "
                + result.Gaps.ToString(CultureInfo.InvariantCulture) + " baseline/unavailable/redacted gaps, "
                + result.OmittedMatches.ToString(CultureInfo.InvariantCulture) + " oversized matching receipts omitted. "
                + (result.Next is null ? "End of snapshot." : "Continue the snapshot, including after an empty page.")
                + " Query is volatile; no activity, authority or question target changed.";
        }
        finally { historySearchCancellation = null; }
    }, passive: true);

    private void ClearHistorySearch()
    {
        historySearchCancellation?.Cancel();
        historySearch = null;
        historyQuery = string.Empty;
        detail = string.Empty;
    }

    private void NotifyHistorySearch()
    {
        OnPropertyChanged(nameof(HistoryQuery));
        OnPropertyChanged(nameof(CanSearchHistory));
        OnPropertyChanged(nameof(CanNextHistorySearch));
        OnPropertyChanged(nameof(CanCancelHistorySearch));
    }
}
