using System.Collections.Immutable;

using Kora.Application.Infrastructure;
using Kora.Core.Authorization;

namespace Kora.Application.Interaction;

public sealed partial class ExactGrantsViewModel
{
    private string historyOutcomeFilter = "Any";
    private string historyInitiatorFilter = "Any";
    private string historyReasonCodeFilter = string.Empty;
    private string historyRetentionFilter = "Any";
    private string historyCommittedFromUtc = string.Empty;
    private string historyCommittedUntilUtc = string.Empty;
    private string historyDueFromUtc = string.Empty;
    private string historyDueUntilUtc = string.Empty;
    private Guid historyAdmissionId;

    public static ImmutableArray<string> HistoryOutcomeFilters { get; } =
        ["Any", "Requested", "Succeeded", "Failed", "Denied", "Cancelled", "Unknown"];
    public static ImmutableArray<ExactGrantHistoryInitiatorOption> HistoryInitiatorFilters => ExactGrantHistoryQuery.InitiatorOptions;
    public static ImmutableArray<string> HistoryRetentionFilters => ExactGrantHistoryQuery.RetentionOptions;
    public AsyncCommand ClearHistoryFiltersCommand { get; }
    public string HistoryOutcomeFilter
    {
        get => historyOutcomeFilter;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (SetProperty(ref historyOutcomeFilter, value)) { HistoryFiltersChanged(); }
        }
    }
    public string HistoryReasonCodeFilter
    {
        get => historyReasonCodeFilter;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (SetProperty(ref historyReasonCodeFilter, value)) { HistoryFiltersChanged(); }
        }
    }
    public string HistoryInitiatorFilter
    {
        get => historyInitiatorFilter;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (SetProperty(ref historyInitiatorFilter, value)) { HistoryFiltersChanged(); }
        }
    }
    public string HistoryRetentionFilter
    {
        get => historyRetentionFilter;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (SetProperty(ref historyRetentionFilter, value)) { HistoryFiltersChanged(); }
        }
    }
    public string HistoryCommittedFromUtc
    {
        get => historyCommittedFromUtc;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (SetProperty(ref historyCommittedFromUtc, value)) { HistoryFiltersChanged(); }
        }
    }
    public string HistoryCommittedUntilUtc
    {
        get => historyCommittedUntilUtc;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (SetProperty(ref historyCommittedUntilUtc, value)) { HistoryFiltersChanged(); }
        }
    }
    public string HistoryDueFromUtc
    {
        get => historyDueFromUtc;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (SetProperty(ref historyDueFromUtc, value)) { HistoryFiltersChanged(); }
        }
    }
    public string HistoryDueUntilUtc
    {
        get => historyDueUntilUtc;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (SetProperty(ref historyDueUntilUtc, value)) { HistoryFiltersChanged(); }
        }
    }

    public void ClearHistoryFilters()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        historyOutcomeFilter = "Any";
        historyInitiatorFilter = "Any";
        historyReasonCodeFilter = string.Empty;
        historyRetentionFilter = "Any";
        historyCommittedFromUtc = string.Empty;
        historyCommittedUntilUtc = string.Empty;
        historyDueFromUtc = string.Empty;
        historyDueUntilUtc = string.Empty;
        OnPropertyChanged(nameof(HistoryOutcomeFilter));
        OnPropertyChanged(nameof(HistoryInitiatorFilter));
        OnPropertyChanged(nameof(HistoryReasonCodeFilter));
        OnPropertyChanged(nameof(HistoryRetentionFilter));
        OnPropertyChanged(nameof(HistoryCommittedFromUtc));
        OnPropertyChanged(nameof(HistoryCommittedUntilUtc));
        OnPropertyChanged(nameof(HistoryDueFromUtc));
        OnPropertyChanged(nameof(HistoryDueUntilUtc));
        HistoryFiltersChanged();
    }

    private void HistoryFiltersChanged()
    {
        var exact = inspectedHistoryTarget;
        var revision = historyControlRevision;
        FiltersChanged();
        // Keep only the explicitly inspected inert target, never actionable inventory selection or review.
        inspectedHistoryTarget = exact;
        historyControlRevision = revision;
        historyAdmissionId = Guid.NewGuid();
        HistorySummary = "History filters changed. Read explicitly for the inspected exact ID; typing performs no read. "
            + "Outcome, initiator, exact canonical reason, retention and inclusive UTC ranges match the complete committed envelope, "
            + "not separately retained Requested review.";
        Message = "History filters changed. Rows, continuation and actionable selection/reviews retired. Fresh explicit Read required.";
        OnPropertyChanged(nameof(Inspection));
        NotifyCommands();
    }
}
