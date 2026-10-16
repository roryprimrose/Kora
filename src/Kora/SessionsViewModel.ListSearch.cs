using System.Globalization;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private string listQuery = string.Empty;
    private string listMinimumGeneration = string.Empty;
    private string listMaximumGeneration = string.Empty;
    private string listMinimumMetadataRevision = string.Empty;
    private string listMaximumMetadataRevision = string.Empty;
    private SessionListSearchKind listSearchKind;
    private SessionListSearchPage? listSearch;
    private CancellationTokenSource? listSearchCancellation;
    private bool listSearchMode;
    private bool listSearching;
    private long listSearchEpoch;

    public IReadOnlyList<SessionListSearchKind> ListSearchKinds { get; } = Enum.GetValues<SessionListSearchKind>();
    public string ListMinimumGeneration
    {
        get => listMinimumGeneration;
        set => ChangeListRange(ref listMinimumGeneration, value);
    }
    public string ListMaximumGeneration
    {
        get => listMaximumGeneration;
        set => ChangeListRange(ref listMaximumGeneration, value);
    }
    public string ListMinimumMetadataRevision
    {
        get => listMinimumMetadataRevision;
        set => ChangeListRange(ref listMinimumMetadataRevision, value);
    }
    public string ListMaximumMetadataRevision
    {
        get => listMaximumMetadataRevision;
        set => ChangeListRange(ref listMaximumMetadataRevision, value);
    }

    private void ChangeListRange(ref string field, string value)
    {
        if (!CanEditListSearch) { return; }
        InvalidateListSearch();
        field = value;
        status = ListQueryHint;
        Notify();
    }
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

    public bool CanEditListSearch => !closed && access.CanInspect && (!busy || listSearching)
        && (!listSearchMode || !ComposerDirty);
    public bool CanSearchList => CanRead && !refreshingWork
        && IsListQueryValid;
    public bool CanNextListSearch => CanSearchList && sessions is not null && listSearch?.Next is not null;
    public bool CanCancelListSearch => !closed && listSearchCancellation is not null;

    private bool IsListQueryValid => SessionListSearch.IsValid(listSearchKind, sessionFilter, listQuery,
        listMinimumGeneration, listMaximumGeneration, listMinimumMetadataRevision, listMaximumMetadataRevision);

    private string ListQueryHint => IsListQueryValid
        ? "Session metadata query changed. Search starts a fresh bounded scan; no history, action or name authority."
        : "Invalid session metadata query. NameSubstring requires nonblank single-line NFC text, no control/format or surrounding whitespace, "
            + "at most 120 scalars / 480 UTF-8 bytes. ExactId requires a nonempty canonical lowercase D GUID. "
            + "Inclusive generation/name-revision bounds require positive decimal integers with minimum <= maximum; empty means Any. "
            + "Name-revision bounds exclude unnamed rows. Nothing is normalized, truncated or searched.";

    public void CancelListSearch() => listSearchCancellation?.Cancel();

    public void ClearListSearch()
    {
        if (!CanEditListSearch) { return; }
        InvalidateListSearch();
        listQuery = string.Empty;
        ClearListRanges();
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
        if (!await ProtectComposerAsync()) { return; }
        var cursor = next ? listSearch?.Next
            ?? throw new InvalidOperationException("No list continuation. Start a fresh Search.") : null;
        var epoch = listSearchEpoch;
        var selection = selectionEpoch;
        var admission = access.ControlRevision;
        var query = SessionListSearch.Parse(listSearchKind, sessionFilter, listQuery,
            listMinimumGeneration, listMaximumGeneration, listMinimumMetadataRevision, listMaximumMetadataRevision);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        listSearchCancellation = cancellation;
        NotifyListSearch();
        try
        {
            var result = await service.SearchListAsync(query.Kind, query.Filter, query.Value, cursor, 25, cancellation.Token,
                query.MinimumGeneration, query.MaximumGeneration, query.MinimumMetadataRevision, query.MaximumMetadataRevision);
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
        if (closed || !access.CanInspect)
        {
            listQuery = string.Empty;
            ClearListRanges();
        }
    }

    private void ClearListRanges()
    {
        listMinimumGeneration = string.Empty;
        listMaximumGeneration = string.Empty;
        listMinimumMetadataRevision = string.Empty;
        listMaximumMetadataRevision = string.Empty;
    }

    private void NotifyListSearch()
    {
        OnPropertyChanged(nameof(ListQuery));
        OnPropertyChanged(nameof(ListMinimumGeneration));
        OnPropertyChanged(nameof(ListMaximumGeneration));
        OnPropertyChanged(nameof(ListMinimumMetadataRevision));
        OnPropertyChanged(nameof(ListMaximumMetadataRevision));
        OnPropertyChanged(nameof(ListSearchKind));
        OnPropertyChanged(nameof(CanEditListSearch));
        OnPropertyChanged(nameof(CanSearchList));
        OnPropertyChanged(nameof(CanNextListSearch));
        OnPropertyChanged(nameof(CanCancelListSearch));
    }
}
