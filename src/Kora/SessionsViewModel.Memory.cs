using System.Text;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private IReadOnlyList<MemorySummary> memoryRows = [];
    private MemorySummary? selectedMemory;
    private MemoryRecord? inspectedMemory;
    private string memoryDetail = string.Empty;
    private string memoryDraft = string.Empty;
    private MemoryContentClass memoryClass = MemoryContentClass.ExplicitFact;
    public IReadOnlyList<MemorySummary> MemoryRecords => memoryRows;
    public MemorySummary? SelectedMemory => selectedMemory;
    public string MemoryDetail => memoryDetail;
    public string MemoryDraft
    {
        get => memoryDraft;
        set { if (!CanReadMemories || notifying) { return; } memoryDraft = value; OnPropertyChanged(); }
    }
    public MemoryContentClass MemoryClass
    {
        get => memoryClass;
        set { if (!CanReadMemories || notifying) { return; } memoryClass = value; OnPropertyChanged(); }
    }
    public IReadOnlyList<MemoryContentClass> MemoryClasses { get; } =
        [MemoryContentClass.ExplicitFact, MemoryContentClass.ResponsePreference, MemoryContentClass.WorkflowPreference, MemoryContentClass.Decision];
    public bool CanReadMemories => CanRead && access.CanControl && selected?.Authority.IsActive == true && memories is not null;
    public bool CanInspectMemory => CanReadMemories && selectedMemory is not null;
    public bool CanProposeMemory => CanReadMemories;
    public bool CanReviewMemory => CanInspectMemory && inspectedMemory?.Review == MemoryReviewState.Proposed
        && inspectedMemory.Candidate is not null && inspectedMemory.Retention != MemoryRetentionState.Forgotten;
    public bool CanAdmitMemory => CanInspectMemory && selectedMemory?.Review == MemoryReviewState.Reviewed
        && selectedMemory.Retention == MemoryRetentionState.Pending;
    public bool CanEditMemory => CanInspectMemory && selectedMemory?.Retention != MemoryRetentionState.Forgotten;
    public bool CanDisableMemory => CanInspectMemory && selectedMemory?.Retention == MemoryRetentionState.Enabled;

    public void SelectMemory(MemorySummary? record)
    {
        if (notifying || !CanReadMemories || record is not null && !memoryRows.Contains(record)) { return; }
        selectedMemory = record;
        inspectedMemory = null;
        memoryDetail = string.Empty;
        memoryDraft = string.Empty;
        selectionEpoch++;
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
        var subject = RequireSelected().Authority.SessionId;
        var epoch = selectionEpoch;
        var target = operation is MemoryCommandOperation.List or MemoryCommandOperation.Propose ? null
            : selectedMemory ?? throw new InvalidOperationException("Select an exact memory from the metadata list.");
        var command = new MemoryCommand(operation, subject.Value, target?.Id, target?.Revision ?? 0, accept,
            operation is MemoryCommandOperation.Edit or MemoryCommandOperation.Propose ? new(memoryClass, memoryDraft) : null);
        var result = await (memories ?? throw new InvalidOperationException("Memory management is unavailable."))
            .ExecuteAsync(command, RequestOrigin.LocalUi,
                () => !closed && access.CanControl && selectionEpoch == epoch && selected?.Authority.SessionId == subject,
                lifetime.Token);
        if (closed || selectionEpoch != epoch || selected?.Authority.SessionId != subject || !access.CanControl)
        {
            throw new OperationCanceledException("Memory presentation admission changed.");
        }
        if (operation == MemoryCommandOperation.List)
        {
            ClearMemories();
            memoryRows = result.Memories;
        }
        else if (result.Inspected is { } inspected)
        {
            inspectedMemory = inspected;
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
        }
        memoryDetail = Encoding.UTF8.GetString(MemoryCommandResult.Serialize(result));
        status = "Memory management " + result.Outcome + ". Inspect exact content/classification, then explicitly review and separately admit."
            + " New user proposals stay volatile until admission; classification is not secrecy detection."
            + " Edits require new review; forget retains a content-free non-reusable identity. No recall or model proposal.";
    });

    private void ClearMemories()
    {
        memoryRows = [];
        selectedMemory = null;
        inspectedMemory = null;
        memoryDraft = string.Empty;
        memoryDetail = string.Empty;
        memories?.ClearInspection();
    }

    private void NotifyMemories()
    {
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
    }
}
