using System.Globalization;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private string listQuery = string.Empty;
    private SessionListSearchKind listSearchKind;
    private SessionListSearchPage? listSearch;
    private CancellationTokenSource? listSearchCancellation;
    private bool listSearchMode;
    private bool listSearching;
    private long listSearchEpoch;

    public IReadOnlyList<SessionListSearchKind> ListSearchKinds { get; } = Enum.GetValues<SessionListSearchKind>();
    public string ListQuery
    {
        get => listQuery;
        set
        {
            if (!CanEditListSearch) { return; }
            InvalidateListSearch();
            listQuery = value;
            status = ListQueryHint;
            Notify();
        }
    }

    public SessionListSearchKind ListSearchKind
    {
        get => listSearchKind;
        set
        {
            if (!CanEditListSearch || !Enum.IsDefined(value)) { return; }
            InvalidateListSearch();
            listSearchKind = value;
            status = ListQueryHint;
            Notify();
        }
    }

    public bool CanEditListSearch => !closed && access.CanInspect && (!busy || listSearching);
    public bool CanSearchList => CanRead && !refreshingWork
        && SessionListSearch.IsValid(listSearchKind, sessionFilter, listQuery);
    public bool CanNextListSearch => CanSearchList && sessions is not null && listSearch?.Next is not null;
    public bool CanCancelListSearch => !closed && listSearchCancellation is not null;

    private string ListQueryHint => SessionListSearch.IsValid(listSearchKind, sessionFilter, listQuery)
        ? "Session metadata query changed. Search starts a fresh bounded scan; no history, action or name authority."
        : "Invalid session metadata query. NameSubstring requires nonblank single-line NFC text, no control/format or surrounding whitespace, "
            + "at most 120 scalars / 480 UTF-8 bytes. ExactId requires a nonempty canonical lowercase D GUID. Nothing is normalized, truncated or searched.";

    public void CancelListSearch() => listSearchCancellation?.Cancel();

    public void ClearListSearch()
    {
        if (!CanEditListSearch) { return; }
        InvalidateListSearch();
        listQuery = string.Empty;
        listSearchMode = false;
        status = "Session metadata search cleared. Refresh returns to ordinary bounded list navigation; no session resumed.";
        Notify();
    }

    public async Task SearchListAsync(bool next = false)
    {
        if (!CanRead || refreshingWork) { return; }
        listSearching = true;
        try
        {
            await RunAsync(() => ReadListSearchAsync(next));
        }
        finally
        {
            listSearching = false;
            NotifyListSearch();
        }
    }

    private async Task ReadListSearchAsync(bool next)
    {
        var cursor = next ? listSearch?.Next
            ?? throw new InvalidOperationException("No list continuation. Start a fresh Search.") : null;
        var epoch = listSearchEpoch;
        var selection = selectionEpoch;
        var admission = access.ControlRevision;
        var query = listQuery;
        var kind = listSearchKind;
        var filter = sessionFilter;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        listSearchCancellation = cancellation;
        NotifyListSearch();
        try
        {
            var result = await service.SearchListAsync(kind, filter, query, cursor, 25, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (closed || epoch != listSearchEpoch || selection != selectionEpoch
                || !access.CanInspect || admission != access.ControlRevision)
            {
                throw new OperationCanceledException("The metadata query, selected subject or source changed. Start a fresh Search.");
            }
            var retained = selected is null ? null : result.Records.FirstOrDefault(record =>
                record.Authority.SessionId == selected.Authority.SessionId);
            if (selected is not null && retained != selected) { ClearSelection(); }
            else if (retained is not null) { selected = retained; }
            sessions = new(result.Records, null);
            listSearch = result;
            listSearchMode = true;
            status = "Passive session metadata: " + result.Records.Length.ToString(CultureInfo.InvariantCulture)
                + " matches; " + result.Scanned.ToString(CultureInfo.InvariantCulture) + " scanned; "
                + result.Unnamed.ToString(CultureInfo.InvariantCulture) + " unnamed; "
                + (result.Scanned - result.Records.Length).ToString(CultureInfo.InvariantCulture)
                + " nonmatches; 0 matching records omitted. "
                + (result.OutputLimited ? "Result/byte bound reached; remaining rows require Next metadata search. "
                    : result.Next is not null ? "Scan bound reached; Next metadata search continues even after zero matches. "
                        : "End of current observation. ")
                + SessionListSearchPage.Scope;
        }
        finally { listSearchCancellation = null; }
    }

    private void InvalidateListSearch()
    {
        listSearchEpoch++;
        listSearchCancellation?.Cancel();
        listSearch = null;
        if (listSearchMode)
        {
            sessions = null;
            ClearSelection();
        }
    }

    private void LeaveListSearch()
    {
        listSearchMode = false;
        InvalidateListSearch();
    }

    private void RevokeListSearchPresentation()
    {
        InvalidateListSearch();
        if (closed || !access.CanInspect) { listQuery = string.Empty; }
    }

    private void NotifyListSearch()
    {
        OnPropertyChanged(nameof(ListQuery));
        OnPropertyChanged(nameof(ListSearchKind));
        OnPropertyChanged(nameof(CanEditListSearch));
        OnPropertyChanged(nameof(CanSearchList));
        OnPropertyChanged(nameof(CanNextListSearch));
        OnPropertyChanged(nameof(CanCancelListSearch));
    }
}
