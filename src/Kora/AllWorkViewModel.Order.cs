using Kora.Core.Storage;

namespace Kora;

internal sealed partial class AllWorkViewModel
{
    private SessionWorkOverviewPageOrder order;
    private IReadOnlyList<SessionWorkOverviewRow> orderedRecords = [];
    private long orderRetirementEpoch = -1;
    private bool notifyingOrder;

    public sealed record OrderChoice(SessionWorkOverviewPageOrder Value, string Label)
    {
        public override string ToString() => Label;
    }

    public IReadOnlyList<OrderChoice> OrderChoices { get; } =
    [
        new(SessionWorkOverviewPageOrder.Source, "Source (unchanged)"),
        new(SessionWorkOverviewPageOrder.SessionIdAscending, "Canonical session ID ascending"),
        new(SessionWorkOverviewPageOrder.SessionIdDescending, "Canonical session ID descending"),
        new(SessionWorkOverviewPageOrder.ObservedFirst, "Observed rows first (category only)"),
        new(SessionWorkOverviewPageOrder.SessionNameAscending, "Session name ascending (Ordinal; unnamed last)"),
        new(SessionWorkOverviewPageOrder.SessionNameDescending, "Session name descending (Ordinal; unnamed last)")
    ];

    public const string OrderScope = "Order this loaded page only; no global ordering, totals or completeness. "
        + "Observed is a presentation category, not ready, allowed, succeeded, healthy, all-clear or currentness; "
        + "it grants no navigation authority, selection or permission. Changing or clearing order performs no read "
        + "and retires exact selection and queued navigation; filters and raw Next are unchanged.";

    public SessionWorkOverviewPageOrder Order
    {
        get => order;
        set => ChangeOrder(value, clear: false);
    }

    private void ChangeOrder(SessionWorkOverviewPageOrder value, bool clear)
    {
        if (!Enum.IsDefined(value)) { throw new ArgumentOutOfRangeException(nameof(value)); }
        if (!CanEdit || notifyingOrder || !clear && order == value) { return; }
        var projection = page is null ? [] : ProjectRows(page, value);
        RetireFind();
        epoch++;
        orderRetirementEpoch = epoch;
        selected = null;
        selectedDetails = string.Empty;
        order = value;
        orderedRecords = projection;
        notifyingOrder = true;
        try { operation?.Cancel(); Notify(); }
        finally { notifyingOrder = false; }
    }

    public OrderChoice SelectedOrderChoice
    {
        get => OrderChoices.Single(choice => choice.Value == order);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Order = value.Value;
        }
    }

    public void ClearOrder() => ChangeOrder(SessionWorkOverviewPageOrder.Source, clear: true);

    // Fresh row instances retire callbacks from an older ordering, even when values and indexes coincide.
    private static SessionWorkOverviewRow[] ProjectRows(SessionWorkOverviewPage source,
        SessionWorkOverviewPageOrder mode) => SessionWorkOverviewOrdering.Project(source, mode)
        .Select(row => row with { }).ToArray();
}
