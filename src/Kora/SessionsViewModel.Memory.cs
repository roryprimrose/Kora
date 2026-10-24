using System.Text;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private IReadOnlyList<MemorySummary> memoryRows = [];
    private IReadOnlyList<MemorySummary> visibleMemoryRows = [];
    private bool memoryListed;
    private long memoryListControl;
    private Kora.Core.Authorization.WorkSessionAuthorization? memorySource;
    private string memoryIdFilter = string.Empty;
    private string memoryScopeIdentityFilter = string.Empty;
    private string memoryMinimumRevisionFilter = string.Empty;
    private string memoryMaximumRevisionFilter = string.Empty;
    private string memoryCreatedFromFilter = string.Empty;
    private string memoryCreatedUntilFilter = string.Empty;
    private MemoryScopeKind? memoryScopeFilter;
    private MemoryReviewState? memoryReviewFilter;
    private MemoryRetentionState? memoryRetentionFilter;
    private MemorySnapshotOrder memoryOrder;
    private bool memoryMutationReady;
    private long memoryEpoch;
    private MemorySummary? selectedMemory;
    private MemoryRecord? inspectedMemory;
    private string memoryDetail = string.Empty;
    private string memoryDraft = string.Empty;
    private MemoryContentClass memoryClass = MemoryContentClass.ExplicitFact;
    public IReadOnlyList<MemorySummary> MemoryRecords => MemorySourceCurrent ? visibleMemoryRows : [];
    private bool MemorySourceCurrent => !closed && access.CanInspect && access.CanControl
        && selected?.Authority.IsActive == true && (memorySource is null
            || selected.Authority == memorySource && access.ControlRevision == memoryListControl);
    public bool CanFilterMemories => MemorySourceCurrent && memoryListed;
    public MemorySnapshotOrder MemoryOrder
    {
        get => memoryOrder;
        set
        {
            if (notifying || !CanFilterMemories || value == memoryOrder) { return; }
            memoryOrder = value;
            ChangeMemoryFilters();
        }
    }
    public IReadOnlyList<MemoryOrderOption> MemoryOrders { get; } =
        [new(MemorySnapshotOrder.Source, "Source order (default)"),
            new(MemorySnapshotOrder.CreatedOldest, "Created oldest"),
            new(MemorySnapshotOrder.CreatedNewest, "Created newest"),
            new(MemorySnapshotOrder.RevisionLowest, "Revision lowest"),
            new(MemorySnapshotOrder.RevisionHighest, "Revision highest")];
    public MemoryOrderOption MemoryOrderChoice
    {
        get => MemoryOrders.FirstOrDefault(option => option.Value == memoryOrder) ?? new(memoryOrder, "Invalid order");
        set { if (value is not null) { MemoryOrder = value.Value; } }
    }
    public string MemoryIdFilter
    {
        get => memoryIdFilter;
        set
        {
            if (notifying || !CanFilterMemories || string.Equals(value, memoryIdFilter, StringComparison.Ordinal)) { return; }
            memoryIdFilter = value;
            ChangeMemoryFilters();
        }
    }
    public MemoryScopeKind? MemoryScopeFilter
    {
        get => memoryScopeFilter;
        set
        {
            if (notifying || !CanFilterMemories || value == memoryScopeFilter) { return; }
            memoryScopeFilter = value;
            ChangeMemoryFilters();
        }
    }
    public string MemoryScopeIdentityFilter
    {
        get => memoryScopeIdentityFilter;
        set => ChangeMemoryRangeFilter(ref memoryScopeIdentityFilter, value);
    }
    public string MemoryMinimumRevisionFilter
    {
        get => memoryMinimumRevisionFilter;
        set => ChangeMemoryRangeFilter(ref memoryMinimumRevisionFilter, value);
    }
    public string MemoryMaximumRevisionFilter
    {
        get => memoryMaximumRevisionFilter;
        set => ChangeMemoryRangeFilter(ref memoryMaximumRevisionFilter, value);
    }
    public string MemoryCreatedFromFilter
    {
        get => memoryCreatedFromFilter;
        set => ChangeMemoryRangeFilter(ref memoryCreatedFromFilter, value);
    }
    public string MemoryCreatedUntilFilter
    {
        get => memoryCreatedUntilFilter;
        set => ChangeMemoryRangeFilter(ref memoryCreatedUntilFilter, value);
    }

    private void ChangeMemoryRangeFilter(ref string field, string value)
    {
        if (notifying || !CanFilterMemories || string.Equals(value, field, StringComparison.Ordinal)) { return; }
        field = value;
        ChangeMemoryFilters();
    }
    public MemoryReviewState? MemoryReviewFilter
    {
        get => memoryReviewFilter;
        set
        {
            if (notifying || !CanFilterMemories || value == memoryReviewFilter) { return; }
            memoryReviewFilter = value;
            ChangeMemoryFilters();
        }
    }
    public MemoryRetentionState? MemoryRetentionFilter
    {
        get => memoryRetentionFilter;
        set
        {
            if (notifying || !CanFilterMemories || value == memoryRetentionFilter) { return; }
            memoryRetentionFilter = value;
            ChangeMemoryFilters();
        }
    }
    public IReadOnlyList<MemoryFilterOption<MemoryScopeKind>> MemoryScopeFilters { get; } =
        [new(null), .. Enum.GetValues<MemoryScopeKind>().Select(value => new MemoryFilterOption<MemoryScopeKind>(value))];
    public IReadOnlyList<MemoryFilterOption<MemoryReviewState>> MemoryReviewFilters { get; } =
        [new(null), .. Enum.GetValues<MemoryReviewState>().Select(value => new MemoryFilterOption<MemoryReviewState>(value))];
    public IReadOnlyList<MemoryFilterOption<MemoryRetentionState>> MemoryRetentionFilters { get; } =
        [new(null), .. Enum.GetValues<MemoryRetentionState>().Select(value => new MemoryFilterOption<MemoryRetentionState>(value))];
    public MemoryFilterOption<MemoryScopeKind> MemoryScopeChoice
    {
        get => MemoryScopeFilters.FirstOrDefault(option => option.Value == memoryScopeFilter) ?? new(memoryScopeFilter);
        set { if (value is not null) { MemoryScopeFilter = value.Value; } }
    }
    public MemoryFilterOption<MemoryReviewState> MemoryReviewChoice
    {
        get => MemoryReviewFilters.FirstOrDefault(option => option.Value == memoryReviewFilter) ?? new(memoryReviewFilter);
        set { if (value is not null) { MemoryReviewFilter = value.Value; } }
    }
    public MemoryFilterOption<MemoryRetentionState> MemoryRetentionChoice
    {
        get => MemoryRetentionFilters.FirstOrDefault(option => option.Value == memoryRetentionFilter) ?? new(memoryRetentionFilter);
        set { if (value is not null) { MemoryRetentionFilter = value.Value; } }
    }
    public string MemoryFilterStatus { get; private set; } = "List explicitly before filtering metadata.";

    public void ClearMemoryFilters()
    {
        if (notifying || !CanFilterMemories) { return; }
        memoryIdFilter = string.Empty;
        memoryScopeIdentityFilter = string.Empty;
        memoryScopeFilter = null;
        memoryReviewFilter = null;
        memoryRetentionFilter = null;
        memoryOrder = MemorySnapshotOrder.Source;
        ClearMemoryRangeFilters();
        ChangeMemoryFilters();
    }

    private void ClearMemoryRangeFilters()
    {
        memoryMinimumRevisionFilter = string.Empty;
        memoryMaximumRevisionFilter = string.Empty;
        memoryCreatedFromFilter = string.Empty;
        memoryCreatedUntilFilter = string.Empty;
    }

    private void ChangeMemoryFilters()
    {
        RetireMemorySelection();
        ApplyMemoryFilters();
        NotifyMemories();
    }

    private void RetireMemorySelection()
    {
        RetireMemoryContext();
        RetireMemoryBatch();
        selectedMemory = null;
        inspectedMemory = null;
        memoryMutationReady = false;
        memoryDetail = string.Empty;
        memoryDraft = string.Empty;
        memories?.ClearInspection();
        memoryEpoch++;
    }

    private void ApplyMemoryFilters()
    {
        try
        {
            var filter = MemoryMetadataFilter.Parse(memoryIdFilter, memoryScopeFilter, memoryReviewFilter, memoryRetentionFilter,
                memoryMinimumRevisionFilter, memoryMaximumRevisionFilter, memoryCreatedFromFilter, memoryCreatedUntilFilter,
                memoryScopeIdentityFilter);
            visibleMemoryRows = filter.Apply(memoryRows, memoryOrder);
            MemoryFilterStatus = $"{visibleMemoryRows.Count} of {memoryRows.Count} snapshot memories. Metadata only; select and inspect again before actions. List explicitly to refresh.";
        }
        catch (InvalidDataException exception)
        {
            visibleMemoryRows = [];
            MemoryFilterStatus = exception.Message;
        }
    }
    public MemorySummary? SelectedMemory => MemorySourceCurrent ? selectedMemory : null;
    public string MemoryDetail => MemorySourceCurrent ? memoryDetail : string.Empty;
    public string MemoryDraft
    {
        get => MemorySourceCurrent ? memoryDraft : string.Empty;
        set
        {
            if (!CanProposeMemory || notifying || string.Equals(memoryDraft, value, StringComparison.Ordinal)) { return; }
            RetireMemoryBatch();
            memoryDraft = value;
            OnPropertyChanged();
            NotifyMemoryBatch();
        }
    }
    public MemoryContentClass MemoryClass
    {
        get => memoryClass;
        set
        {
            if (!CanReadMemories || notifying || memoryClass == value) { return; }
            RetireMemoryBatch();
            memoryClass = value;
            OnPropertyChanged();
            NotifyMemoryBatch();
        }
    }
    public IReadOnlyList<MemoryContentClass> MemoryClasses { get; } =
        [MemoryContentClass.ExplicitFact, MemoryContentClass.ResponsePreference, MemoryContentClass.WorkflowPreference, MemoryContentClass.Decision];
    public bool CanReadMemories => CanRead && access.CanControl && selected?.Authority.IsActive == true && memories is not null;
    public bool CanInspectMemory => CanReadMemories && MemorySourceCurrent && selectedMemory is not null;
    public bool CanProposeMemory => CanReadMemories && MemorySourceCurrent;
    public bool CanReviewMemory => CanInspectMemory && inspectedMemory?.Review == MemoryReviewState.Proposed
        && inspectedMemory.Candidate is not null && inspectedMemory.Retention != MemoryRetentionState.Forgotten;
    public bool CanAdmitMemory => CanInspectMemory && memoryMutationReady && selectedMemory?.Review == MemoryReviewState.Reviewed
        && selectedMemory.Retention == MemoryRetentionState.Pending;
    public bool CanEditMemory => CanInspectMemory && memoryMutationReady && selectedMemory?.Retention != MemoryRetentionState.Forgotten;
    public bool CanDisableMemory => CanInspectMemory && memoryMutationReady && selectedMemory?.Retention == MemoryRetentionState.Enabled;

    public void SelectMemory(MemorySummary? record)
    {
        if (notifying || !CanReadMemories || record is not null && !MemoryRecords.Contains(record)) { return; }
        RetireMemorySelection();
        selectedMemory = record;
        NotifyMemories();
    }

    public Task ListMemoriesAsync() => RunMemoryAsync(MemoryCommandOperation.List);
    public Task ProposeMemoryAsync() => RunMemoryAsync(MemoryCommandOperation.Propose);
    public Task InspectMemoryAsync() => RunMemoryAsync(MemoryCommandOperation.Inspect);
    public Task ReviewMemoryAsync(bool accept) => RunMemoryAsync(MemoryCommandOperation.Review, accept);
    public Task AdmitMemoryAsync() => RunMemoryAsync(MemoryCommandOperation.Admit);
    public Task EditMemoryAsync() => RunMemoryAsync(MemoryCommandOperation.Edit);
    public Task DisableMemoryAsync() => RunMemoryAsync(MemoryCommandOperation.Disable);
    public Task ForgetMemoryAsync() => RunMemoryAsync(MemoryCommandOperation.Forget);

    private Task RunMemoryAsync(MemoryCommandOperation operation, bool accept = false) => RunAsync(async () =>
    {
        RetireMemoryBatch();
        var subject = RequireSelected().Authority.SessionId;
        var authority = RequireSelected().Authority;
        var control = access.ControlRevision;
        var epoch = selectionEpoch;
        var targetEpoch = memoryEpoch;
        if (operation != MemoryCommandOperation.List && !MemorySourceCurrent)
        {
            throw new InvalidOperationException("Memory metadata source changed. List again under current private session admission.");
        }
        var target = operation is MemoryCommandOperation.List or MemoryCommandOperation.Propose ? null
            : selectedMemory ?? throw new InvalidOperationException("Select an exact memory from the metadata list.");
        if (target is not null && (!MemoryRecords.Contains(target)
            || operation != MemoryCommandOperation.Inspect && !memoryMutationReady))
        {
            throw new InvalidOperationException("Select and inspect an exact visible memory before a fresh action.");
        }
        var command = new MemoryCommand(operation, subject.Value, target?.Id, target?.Revision ?? 0, accept,
            operation is MemoryCommandOperation.Edit or MemoryCommandOperation.Propose ? new(memoryClass, memoryDraft) : null);
        var result = await (memories ?? throw new InvalidOperationException("Memory management is unavailable."))
            .ExecuteAsync(command, RequestOrigin.LocalUi,
                () => !closed && access.CanControl && access.ControlRevision == control
                    && selectionEpoch == epoch && memoryEpoch == targetEpoch && selected?.Authority == authority,
                lifetime.Token);
        if (closed || selectionEpoch != epoch || memoryEpoch != targetEpoch || selected?.Authority != authority
            || !access.CanControl || access.ControlRevision != control)
        {
            throw new OperationCanceledException("Memory presentation admission changed.");
        }
        memorySource = authority;
        memoryListControl = control;
        if (operation == MemoryCommandOperation.List)
        {
            RetireMemorySelection();
            memoryRows = result.Memories;
            memoryListed = true;
            ApplyMemoryFilters();
        }
        else if (result.Inspected is { } inspected)
        {
            inspectedMemory = inspected;
            memoryMutationReady = true;
            memoryDraft = inspected.Candidate?.Value ?? string.Empty;
            memoryClass = inspected.Candidate?.ContentClass ?? MemoryContentClass.ExplicitFact;
        }
        else
        {
            inspectedMemory = null;
            memoryDraft = string.Empty;
            if (result.Memories.SingleOrDefault() is { } updated)
            {
                if (operation == MemoryCommandOperation.Propose)
                {
                    memoryRows = [.. memoryRows, updated];
                    selectedMemory = null;
                }
                else
                {
                    memoryRows = [.. memoryRows.Select(row => row.Id == updated.Id ? updated : row)];
                    selectedMemory = updated;
                }
            }
            ApplyMemoryFilters();
            if (selectedMemory is not null && !visibleMemoryRows.Contains(selectedMemory))
            {
                RetireMemorySelection();
            }
        }
        memoryDetail = Encoding.UTF8.GetString(MemoryCommandResult.Serialize(result));
        status = "Memory management " + result.Outcome + ". Inspect exact content/classification, then explicitly review and separately admit."
            + " New user proposals stay volatile until admission; classification is not secrecy detection."
            + " Edits require new review; forget retains a content-free non-reusable identity. No recall or model proposal.";
    });

    private void ClearMemories()
    {
        ClearMemoryContext();
        memoryRows = [];
        visibleMemoryRows = [];
        memoryListed = false;
        memorySource = null;
        memoryIdFilter = string.Empty;
        memoryScopeIdentityFilter = string.Empty;
        memoryScopeFilter = null;
        memoryReviewFilter = null;
        memoryRetentionFilter = null;
        memoryOrder = MemorySnapshotOrder.Source;
        ClearMemoryRangeFilters();
        MemoryFilterStatus = "List explicitly before filtering metadata.";
        RetireMemorySelection();
    }

    private void NotifyMemories()
    {
        var previous = notifying;
        notifying = true;
        try
        {
            NotifyMemoryContext();
            OnPropertyChanged(nameof(MemoryRecords));
            OnPropertyChanged(nameof(SelectedMemory));
            OnPropertyChanged(nameof(MemoryDetail));
            OnPropertyChanged(nameof(MemoryDraft));
            OnPropertyChanged(nameof(MemoryClass));
            OnPropertyChanged(nameof(CanReadMemories));
            OnPropertyChanged(nameof(CanProposeMemory));
            OnPropertyChanged(nameof(CanInspectMemory));
            OnPropertyChanged(nameof(CanReviewMemory));
            OnPropertyChanged(nameof(CanAdmitMemory));
            OnPropertyChanged(nameof(CanEditMemory));
            OnPropertyChanged(nameof(CanDisableMemory));
            OnPropertyChanged(nameof(CanFilterMemories));
            OnPropertyChanged(nameof(MemoryIdFilter));
            OnPropertyChanged(nameof(MemoryScopeIdentityFilter));
            OnPropertyChanged(nameof(MemoryMinimumRevisionFilter));
            OnPropertyChanged(nameof(MemoryMaximumRevisionFilter));
            OnPropertyChanged(nameof(MemoryCreatedFromFilter));
            OnPropertyChanged(nameof(MemoryCreatedUntilFilter));
            OnPropertyChanged(nameof(MemoryScopeFilter));
            OnPropertyChanged(nameof(MemoryScopeChoice));
            OnPropertyChanged(nameof(MemoryReviewFilter));
            OnPropertyChanged(nameof(MemoryRetentionFilter));
            OnPropertyChanged(nameof(MemoryReviewChoice));
            OnPropertyChanged(nameof(MemoryRetentionChoice));
            OnPropertyChanged(nameof(MemoryOrder));
            OnPropertyChanged(nameof(MemoryOrderChoice));
            OnPropertyChanged(nameof(MemoryFilterStatus));
            NotifyMemoryBatch();
        }
        finally { notifying = previous; }
    }

    internal sealed record MemoryFilterOption<T>(T? Value) where T : struct, Enum
    {
        public override string ToString() => Value?.ToString() ?? "Any";
    }

    internal sealed record MemoryOrderOption(MemorySnapshotOrder Value, string Label)
    {
        public override string ToString() => Label;
    }
}
