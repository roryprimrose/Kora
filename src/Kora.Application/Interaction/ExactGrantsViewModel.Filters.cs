using System.Collections.Immutable;
using System.Globalization;

using Kora.Application.Infrastructure;
using Kora.Core.Authorization;

namespace Kora.Application.Interaction;

public sealed partial class ExactGrantsViewModel
{
    private string scopeFilter = "Any";
    private string statusFilter = "Any";
    private string actionIdFilter = string.Empty;
    private string originatingSessionFilter = string.Empty;
    private long filterVersion;
    private CancellationTokenSource? currentOperation;
    private string inventorySummary = FilterDisclosure;

    public const string FilterDisclosure = "Optional exact filters inspect stored scope/status, canonical lowercase ASCII ActionId "
        + "and nonempty lowercase D originating-session GUID. Any/empty defaults preserve the unfiltered inventory. "
        + "Origin is correlation only, not current session authority. Each request advances at most 50 stored rows, "
        + "validating at most 51 including deferred lookahead; returns at most 50 matches / 64 KiB. "
        + "Empty matching pages may still have Next. No applicability, execution or new approval is inferred.";
    public static ImmutableArray<string> ScopeFilters { get; } = ["Any", "Once", "Session", "Perpetual"];
    public static ImmutableArray<string> StatusFilters { get; } = ["Any", "Active", "Consumed", "Revoked"];
    public AsyncCommand ClearFiltersCommand { get; }
    public string InventorySummary { get => inventorySummary; private set => SetProperty(ref inventorySummary, value); }
    public string ScopeFilter
    {
        get => scopeFilter;
        set { ObjectDisposedException.ThrowIf(disposed, this); if (SetProperty(ref scopeFilter, value)) { FiltersChanged(); } }
    }
    public string StatusFilter
    {
        get => statusFilter;
        set { ObjectDisposedException.ThrowIf(disposed, this); if (SetProperty(ref statusFilter, value)) { FiltersChanged(); } }
    }
    public string ActionIdFilter
    {
        get => actionIdFilter;
        set { ObjectDisposedException.ThrowIf(disposed, this); if (SetProperty(ref actionIdFilter, value)) { FiltersChanged(); } }
    }
    public string OriginatingSessionFilter
    {
        get => originatingSessionFilter;
        set { ObjectDisposedException.ThrowIf(disposed, this); if (SetProperty(ref originatingSessionFilter, value)) { FiltersChanged(); } }
    }

    public void ClearFilters()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        scopeFilter = statusFilter = "Any";
        actionIdFilter = originatingSessionFilter = string.Empty;
        OnPropertyChanged(nameof(ScopeFilter));
        OnPropertyChanged(nameof(StatusFilter));
        OnPropertyChanged(nameof(ActionIdFilter));
        OnPropertyChanged(nameof(OriginatingSessionFilter));
        FiltersChanged();
    }

    private void FiltersChanged()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        filterVersion++;
        selectionVersion++;
        currentOperation?.Cancel();
        selected = preview = null;
        next = null;
        ClearHistory();
        ClearBatch();
        batchSelection = [];
        Reviewed = false;
        Records = [];
        InventorySummary = FilterDisclosure;
        Message = "Filters changed. Prior results, selection, history and single/batch confirmations retired. Refresh to validate and read a new exact query.";
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(Inspection));
        NotifyCommands();
    }

    private void DescribeInventory(ExactGrantPage page)
    {
        InventorySummary = "Matches in this page: " + page.Records.Length.ToString(CultureInfo.InvariantCulture)
            + "; validated stored rows (including deferred lookahead): " + page.ScannedRows.ToString(CultureInfo.InvariantCulture)
            + "; advanced rows omitted by exact predicates: " + page.OmittedRecords.ToString(CultureInfo.InvariantCulture)
            + "; advanced through exact ID: " + (page.ScannedThrough?.ToString("D") ?? "none")
            + "; row scan bound reached: " + page.ScanLimitReached
            + "; continuation: " + (page.Next is null ? "end of this snapshot" : "more stored rows, not necessarily matches")
            + Environment.NewLine + FilterDisclosure;
    }

    // A cancelled superseded filter operation must not erase a newer query's presentation via the command error handler.
    private bool IsRetiredFilter(long version) => !disposed && version != filterVersion;

    private void EndOperation()
    {
        currentOperation = null;
        Busy = false;
    }
}
