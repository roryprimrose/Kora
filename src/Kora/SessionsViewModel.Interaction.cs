using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    public string InteractionTarget => selected is { } target
        ? target.Metadata?.Name.Value ?? "Unnamed conversation"
        : "Select a conversation";

    public string SessionNameQuery
    {
        get => ListQuery;
        set
        {
            if (!CanEditListSearch) { return; }
            PrepareSessionNameSearch();
            ListQuery = value;
        }
    }

    public bool CanSearchSessionNames => CanRead && !refreshingWork
        && SessionListSearch.IsValid(SessionListSearchKind.NameSubstring, sessionFilter, listQuery);

    public bool CanNextSessionNameSearch => CanNextListSearch
        && listSearchKind == SessionListSearchKind.NameSubstring && listNameAvailability == SessionNameAvailability.Any;

    public Task SearchSessionNamesAsync(bool next = false)
    {
        PrepareSessionNameSearch();
        return SearchListAsync(next);
    }

    private void PrepareSessionNameSearch()
    {
        if (ListSearchKind != SessionListSearchKind.NameSubstring) { ListSearchKind = SessionListSearchKind.NameSubstring; }
        if (ListNameAvailability != SessionNameAvailability.Any) { ListNameAvailability = SessionNameAvailability.Any; }
    }

    private void NotifySessionNameSearch()
    {
        OnPropertyChanged(nameof(SessionNameQuery));
        OnPropertyChanged(nameof(CanSearchSessionNames));
        OnPropertyChanged(nameof(CanNextSessionNameSearch));
    }

    internal bool HasSessionListPage => sessions is not null;

    internal void ReportNavigationStatus(string reason)
    {
        status = reason;
        Notify();
    }

    internal Func<bool> CapturePresentedRow(SessionWorkspaceEntry row)
    {
        var page = sessions;
        var control = access.ControlRevision;
        var epoch = listSearchEpoch;
        var filter = sessionFilter;
        var order = listPageOrderRevision;
        return () => !closed && access.CanInspect && control == access.ControlRevision
            && ReferenceEquals(sessions, page) && epoch == listSearchEpoch
            && filter == sessionFilter && order == listPageOrderRevision
            && Sessions.Any(entry => ReferenceEquals(entry, row));
    }
}
