using System.Collections.Immutable;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionListPageOrder listPageOrder;
    private ImmutableArray<SessionWorkspaceEntry> orderedListPage = [];
    private long listPageControlRevision;

    public IReadOnlyList<SessionListPageOrder> ListPageOrders { get; } = Enum.GetValues<SessionListPageOrder>();
    public string ListPageOrderScope => "Order this loaded session page only: paged metadata search (including empty-text Browse), "
        + "not the ordinary Refresh list or all sessions. Source preserves the original page. "
        + "Names use case-sensitive ordinal order; unnamed last in both directions. "
        + "Ties use canonical session ID ascending. No read, selection, saved preference or global completeness claim; Next keeps the original scan.";
    public bool CanOrderListPage => CanRead && !refreshingWork && !conversationBusy
        && listSearchMode && listSearch is not null && sessions?.Records == listSearch.Records
        && listPageControlRevision == access.ControlRevision;
    public SessionListPageOrder ListPageOrder
    {
        get => listPageOrder;
        set
        {
            if (notifying || !CanOrderListPage) { return; }
            try
            {
                var projection = SessionListPageOrdering.Apply(listSearch!.Records, value);
                orderedListPage = projection;
                listPageOrder = value;
                status = "Loaded session metadata page order changed only. Exact selection and unsaved buffers retained; no read or action.";
            }
            catch (InvalidDataException exception)
            {
                InvalidateListSearch();
                status = "Session page order unavailable: " + exception.Message + " Search explicitly to recover.";
                Failure(logger, exception.GetType().Name);
            }
            Notify();
        }
    }

    public void ClearListPageOrder() => ListPageOrder = SessionListPageOrder.Source;

    private void NotifyListPageOrder()
    {
        OnPropertyChanged(nameof(ListPageOrder));
        OnPropertyChanged(nameof(CanOrderListPage));
    }
}
