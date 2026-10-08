using System.Globalization;
using System.Text;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SessionsViewModel(
    SessionWorkspaceService service, DurableEvidenceQuery evidence, ISessionWorkspaceAccess access,
    ILogger<SessionsViewModel> logger) : ObservableObject
{
    private readonly HostRequest viewer = HostRequest.Create(RequestOrigin.LocalUi);
    private readonly CancellationTokenSource lifetime = new();
    private SessionPage<SessionWorkspaceEntry>? sessions;
    private SessionPage<HostQuestionRecord>? questions;
    private SessionPage<HostTaskRecord>? tasks;
    private EvidencePage? evidencePage;
    private SessionWorkspaceEntry? selected;
    private HostTaskRecord? selectedTask;
    private HostTaskObservation? inspectedTask;
    private SessionDispositionPreview? dispositionPreview;
    private SessionHistoryPage? history;
    private string historySessionId = string.Empty;
    private string nameDraft = string.Empty;
    private string status = "Refresh to inspect durable names and authority, or enter an exact ID for bounded passive interaction history. No composer or queue is available.";
    private string detail = string.Empty;
    private bool busy;
    private bool closed;

    public IReadOnlyList<SessionWorkspaceEntry> Sessions => sessions?.Records ?? [];
    public IReadOnlyList<HostTaskRecord> TaskRecords => tasks?.Records ?? [];
    public bool CanInspectTask => CanRead && selectedTask is not null;
    public bool CanCancelTask => CanInspectTask && inspectedTask is
        { CurrentSource: true, Task.State: HostTaskState.IntentRecorded, Question.Status: QuestionStatus.Pending };

    public void SelectTask(HostTaskRecord? task)
    {
        if (!CanRead || (task is not null && !TaskRecords.Contains(task))) { return; }
        selectedTask = task;
        inspectedTask = null;
        Notify();
    }

    public Task InspectTaskAsync() => RunAsync(async () =>
    {
        var result = await service.ExecuteCommandAsync(new(SessionCommandOperation.TaskInspect,
            RequireSelected().Authority.SessionId.Value)
            { TaskId = selectedTask?.Request.TaskId.Value ?? throw new InvalidOperationException("Select an exact task from this page.") },
            RequestOrigin.LocalUi, () => !closed && access.CanInspect, lifetime.Token);
        inspectedTask = result.TaskDetails.SingleOrDefault();
        detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(result));
        status = "Exact task observation. Inspect is passive; cancellation is a separate deliberate action using these revisions.";
    });

    public Task CancelTaskAsync() => RunAsync(async () =>
    {
        var target = inspectedTask ?? throw new InvalidOperationException("Inspect the exact selected task before cancellation.");
        var question = target.Question ?? throw new InvalidOperationException("No admitted question wait exists.");
        var result = await service.CancelTaskAsync(new(target.Task.Request.SessionId, target.Task.Request.TaskId,
            target.Task.Revision, target.Generation, question.Key.QuestionId, question.Key.Revision),
            RequestOrigin.LocalUi, () => !closed && access.CanInspect, lifetime.Token);
        inspectedTask = result;
        detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(new("committed",
            "Exact local-version wait cancelled before dispatch; no effect termination claimed.") { TaskDetails = [result] }));
        status = "Durable terminal task, question cancellation and required audit committed atomically. Refresh to observe other work.";
    });
    public string NameDraft
    {
        get => nameDraft;
        set { nameDraft = value; OnPropertyChanged(); Notify(); }
    }
    public string Status => status;
    public string Detail => detail;
    public string HistorySessionId
    {
        get => historySessionId;
        set { historySessionId = value; history = null; OnPropertyChanged(); Notify(); }
    }
    public bool CanHistory => CanRead && Guid.TryParseExact(historySessionId, "D", out var id) && id != Guid.Empty;
    public bool CanNextHistory => CanHistory && history?.Next is not null;

    public Task ReadHistoryAsync(bool next = false) => RunAsync(async () =>
    {
        if (!Guid.TryParseExact(historySessionId, "D", out var id) || id == Guid.Empty)
        {
            throw new InvalidOperationException("Enter the exact immutable session ID, not a name.");
        }
        history = await service.ReadHistoryAsync(new(id),
            next ? history?.Next ?? throw new InvalidOperationException("No next history page.") : null, 25, lifetime.Token);
        detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(new("observed", SessionHistoryPage.Scope) { History = history }));
        status = "Passive ordered history for exact ID " + id.ToString("D")
            + (history.Disposed ? ". Disposed: content redacted; immutable citations retained." : ". No session activity, reply, focus or voice target changed.");
    });
    public bool CanRead => !busy && !closed && access.CanInspect;
    public bool CanNext => CanRead && sessions?.Next is not null;
    public bool CanNextQuestions => CanRead && questions?.Next is not null;
    public bool CanNextTasks => CanRead && tasks?.Next is not null;
    public bool CanEvidence => CanRead && selected is not null;
    public bool CanNextEvidence => CanEvidence && evidencePage?.Cursor is not null;
    public bool CanDone => CanRead && access.CanControl && selected?.Authority.IsActive == true;
    public bool CanResume => CanRead && access.CanControl && selected?.Authority.IsActive == false;
    public bool CanCreate => CanRead && access.CanControl && !string.IsNullOrWhiteSpace(nameDraft);
    public bool CanRename => CanCreate && selected is not null;
    public bool CanPreviewDisposition => CanRead && access.CanControl && selected is not null;
    public bool CanConfirmDisposition => CanPreviewDisposition && dispositionPreview is not null;

    public Task PreviewDispositionAsync() => RunAsync(async () =>
    {
        dispositionPreview = null;
        var target = RequireSelected();
        var preview = await service.PreviewDispositionAsync(target.Authority.SessionId, target.Authority.Generation,
            target.Metadata?.Revision.Value ?? 0, lifetime.Token);
        dispositionPreview = preview;
        detail = "Exact session ID: " + preview.Session.Authority.SessionId.Value.ToString("D")
            + "\nName (label only): " + (preview.Session.Metadata?.Name.Value ?? "Unnamed")
            + "\nGeneration: " + preview.Session.Authority.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + "\nMetadata revision: " + (preview.Session.Metadata?.Revision.Value ?? 0).ToString(CultureInfo.InvariantCulture)
            + "\nLive rows to remove: questions " + preview.Questions.ToString(CultureInfo.InvariantCulture)
            + ", scoped grants " + preview.ScopedGrants.ToString(CultureInfo.InvariantCulture)
            + ", observations " + preview.Observations.ToString(CultureInfo.InvariantCulture)
            + ", waits " + preview.Waits.ToString(CultureInfo.InvariantCulture)
            + "\n" + SessionDispositionPreview.Scope;
        status = "Preview only; nothing removed. Confirm logical disposition is a separate deliberate action for this exact ID and revision.";
    });

    public Task ConfirmDispositionAsync() => RunAsync(async () =>
    {
        var preview = dispositionPreview ?? throw new InvalidOperationException("Preview the exact session first.");
        dispositionPreview = null;
        var receipt = await service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi,
            () => !closed && access.CanControl, lifetime.Token);
        sessions = null;
        ClearSelection();
        detail = SessionDispositionPreview.Scope;
        status = "Committed logical disposition for " + receipt.SessionId.Value.ToString("D")
            + " at tombstone generation " + receipt.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + ". Live rows removed; retained provenance and recoverable copies are not erased. Refresh to inspect unrelated sessions.";
    });

    public Task RefreshAsync() => RunAsync(async () =>
    {
        sessions = await service.ReadMetadataAsync(null, 25, lifetime.Token);
        ClearSelection();
        status = "Active/Done minimal authority: " + sessions.Records.Length.ToString(CultureInfo.InvariantCulture)
            + " records on this page. " + (sessions.Next is null ? "End of current list." : "More pages available.");
    });

    public Task NextAsync() => RunAsync(async () =>
    {
        sessions = await service.ReadMetadataAsync(sessions?.Next
            ?? throw new InvalidOperationException("Refresh before requesting a next page."), 25, lifetime.Token);
        ClearSelection();
        status = "Next bounded authority page. Refresh for concurrent additions/changes.";
    });

    public Task SelectAsync(SessionWorkspaceEntry? record) => RunAsync(async () =>
    {
        if (record is not null && !Sessions.Contains(record))
        {
            throw new InvalidOperationException("Select an existing record from this page.");
        }
        ClearSelection();
        selected = record;
        if (record is null) { return; }
        nameDraft = record.Metadata?.Name.Value ?? string.Empty;
        historySessionId = record.Authority.SessionId.Value.ToString("D");
        questions = await service.ReadQuestionsAsync(record.Authority.SessionId, null, 25, lifetime.Token);
        tasks = await service.ReadTasksAsync(record.Authority.SessionId, null, 25, lifetime.Token);
        Render();
        status = "Passive selected detail. Selection never targets a question, approval or command. Pages are observations, not an atomic work ledger.";
    });

    public Task NextQuestionsAsync() => RunAsync(async () =>
    {
        questions = await service.ReadQuestionsAsync(RequireSelected().Authority.SessionId,
            questions?.Next ?? throw new InvalidOperationException("No next question page."), 25, lifetime.Token);
        Render();
    });

    public Task NextTasksAsync() => RunAsync(async () =>
    {
        selectedTask = null;
        inspectedTask = null;
        tasks = await service.ReadTasksAsync(RequireSelected().Authority.SessionId,
            tasks?.Next ?? throw new InvalidOperationException("No next task page."), 25, lifetime.Token);
        Render();
    });

    public Task ReadEvidenceAsync(bool next = false) => RunAsync(async () =>
    {
        evidencePage = await evidence.QueryAsync(new() { SessionId = RequireSelected().Authority.SessionId },
            next ? evidencePage?.Cursor ?? throw new InvalidOperationException("No next evidence page.") : null, lifetime.Token);
        Render();
    });

    public Task ChangeLifecycleAsync(bool active) => RunAsync(async () =>
    {
        var target = RequireSelected();
        var changed = await service.ChangeLifecycleAsync(target.Authority.SessionId, target.Authority.Generation, active,
            RequestOrigin.LocalUi, lifetime.Token);
        selected = new(changed, target.Metadata);
        questions = await service.ReadQuestionsAsync(changed.SessionId, null, 25, lifetime.Token);
        tasks = await service.ReadTasksAsync(changed.SessionId, null, 25, lifetime.Token);
        evidencePage = null;
        Render();
        status = "Committed " + (active ? "resume" : "Done") + " for " + changed.SessionId.Value.ToString("D")
            + ". Generation advanced. No task, approval or context replay. Refresh the list to observe the new state.";
    });

    public Task CreateAsync() => RunAsync(async () =>
    {
        var created = await service.CreateAsync(new(nameDraft), RequestOrigin.LocalUi, lifetime.Token);
        sessions = null;
        ClearSelection();
        selected = created;
        nameDraft = created.Metadata!.Name.Value;
        Render();
        status = "Committed empty Active session " + created.Authority.SessionId.Value.ToString("D")
            + ". No executor, model context, work dispatch or permission created. Refresh to list it.";
    });

    public Task RenameAsync() => RunAsync(async () =>
    {
        var target = RequireSelected();
        selected = await service.RenameAsync(target.Authority.SessionId, target.Authority.Generation,
            target.Metadata?.Revision.Value ?? 0, new(nameDraft), RequestOrigin.LocalUi, lifetime.Token);
        Render();
        status = "Committed rename for exact ID " + selected.Authority.SessionId.Value.ToString("D")
            + ". Lifecycle, generation, work and approvals unchanged. Refresh for the current list.";
    });

    private SessionWorkspaceEntry RequireSelected() =>
        selected ?? throw new InvalidOperationException("Select the exact subject session first.");

    private void Render()
    {
        var entry = RequireSelected();
        var record = entry.Authority;
        var text = new StringBuilder().Append("Session ").Append(record.SessionId.Value.ToString("D"))
            .Append(record.IsActive ? " | Active" : " | Done").Append(" | generation ").Append(record.Generation.Value)
            .AppendLine().Append("Name: ").AppendLine(entry.Metadata?.Name.Value ?? "Unnamed session (metadata not yet set)")
            .Append("Metadata revision: ").Append(entry.Metadata?.Revision.Value ?? 0).AppendLine()
            .AppendLine("Name is user content, never authority. No conversation, queue, scheduler or restored context.")
            .AppendLine("Task records (current durable state, not inferred runtime progress):");
        foreach (var task in tasks?.Records ?? [])
        {
            text.Append(task.Request.TaskId.Value.ToString("D")).Append(" | ").Append(task.State)
                .Append(" | revision ").Append(task.Revision.Value).Append(" | origin ").Append(task.Request.Origin).AppendLine();
        }
        text.AppendLine(tasks?.Next is null ? "End of observed task page set." : "More task records available.");
        text.AppendLine("Typed questions (history only; no reply/review controls):");
        foreach (var question in questions?.Records ?? [])
        {
            text.Append(question.Key.QuestionId.Value.ToString("D")).Append(" | ").Append(question.Status)
                .Append(" | revision ").Append(question.Key.Revision.Value).Append(" | generation ").Append(question.SessionGeneration.Value)
                .Append(" | request ").Append(question.Key.Request.RequestId.Value.ToString("D"))
                .Append(" | task ").Append(question.Key.Request.TaskId.Value.ToString("D"))
                .Append(" | origin ").Append(question.Key.Request.Origin).Append(" | answer channel ").Append(question.AnswerChannel)
                .Append(" | expires ").Append(question.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)).AppendLine()
                .Append(question.Spec.Kind).Append(": ").AppendLine(question.Spec.Text);
            foreach (var option in question.Spec.Options) { text.Append(option.Id).Append(": ").AppendLine(option.Label); }
            if (question.Draft is { } draft)
            {
                text.Append("Recorded draft/answer: ").AppendJoin(", ", draft.Choices).Append(' ').AppendLine(draft.Text);
            }
            if (question.Proposal is { } proposal) { text.Append("Exact proposal: ").Append(proposal.ProposalId.Value.ToString("D")).AppendLine(); }
        }
        text.AppendLine(questions?.Next is null ? "End of observed question page set." : "More questions available.");
        text.AppendLine("Evidence is a separate diagnostic/audit projection, never authoritative approval or conversation.");
        if (evidencePage is { } page)
        {
            text.Append(page.Status).AppendLine().AppendLine(Encoding.UTF8.GetString(DurableEvidenceQuery.Serialize(page)));
        }
        else { text.AppendLine("Evidence not queried. Use Read selected evidence."); }
        text.AppendLine(DurableVersionQuery.StorageDisclosure);
        detail = text.ToString();
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (busy || closed) { return; }
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), viewer.SessionId,
            viewer.TaskId, RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        busy = true;
        Notify();
        try
        {
            if (!access.CanInspect) { throw new InvalidOperationException("Private desktop ownership is unavailable."); }
            await operation();
            if (closed || !access.CanInspect) { throw new OperationCanceledException("Private presentation closed."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            sessions = null;
            ClearSelection();
            status = "Cancelled or privacy closed; no late content or rollback of a possible committed lifecycle is claimed. Refresh durable state before retrying.";
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            sessions = null;
            ClearSelection();
            status = "Sessions unavailable/denied: " + exception.Message
                + " Refresh durable state before retrying; no empty success, abandoned work or replay is claimed.";
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            busy = false;
            if (closed) { lifetime.Dispose(); }
            Notify();
        }
    }

    private void ClearSelection()
    {
        dispositionPreview = null;
        selected = null;
        selectedTask = null;
        inspectedTask = null;
        questions = null;
        tasks = null;
        evidencePage = null;
        history = null;
        detail = string.Empty;
        nameDraft = string.Empty;
    }

    public void Close()
    {
        if (closed) { return; }
        closed = true;
        lifetime.Cancel();
        if (!busy) { lifetime.Dispose(); }
        sessions = null;
        ClearSelection();
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Sessions));
        OnPropertyChanged(nameof(TaskRecords));
        OnPropertyChanged(nameof(CanInspectTask));
        OnPropertyChanged(nameof(CanCancelTask));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(NameDraft));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanRename));
        OnPropertyChanged(nameof(CanPreviewDisposition));
        OnPropertyChanged(nameof(CanConfirmDisposition));
        OnPropertyChanged(nameof(CanRead));
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanNextQuestions));
        OnPropertyChanged(nameof(CanNextTasks));
        OnPropertyChanged(nameof(CanEvidence));
        OnPropertyChanged(nameof(CanNextEvidence));
        OnPropertyChanged(nameof(CanDone));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(HistorySessionId));
        OnPropertyChanged(nameof(CanHistory));
        OnPropertyChanged(nameof(CanNextHistory));
    }
}
