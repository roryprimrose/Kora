using System.Globalization;
using System.Text;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.Infrastructure;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Presentation;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SessionsViewModel(
    SessionWorkspaceService service, DurableEvidenceQuery evidence, ISessionWorkspaceAccess access,
    ILogger<SessionsViewModel> logger, LocalEventBroker? localEvents = null,
    Func<AdmittedDetailContent, string>? openHistoryDetail = null,
    Action<HostId<SessionIdentity>>? revokeHistoryDetails = null,
    Kora.Application.Memory.MemoryManagementService? memories = null) : ObservableObject
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
    private SessionHistoryEvent? selectedHistory;
    private string historySessionId = string.Empty;
    private string nameDraft = string.Empty;
    private string status = "Refresh to inspect exact sessions and passive history. The deterministic queue admits fixed local-version reads only; no model-assisted routing or general executor.";
    private string detail = string.Empty;
    private bool busy;
    private bool refreshingWork;
    private bool closed;
    private bool notifying;

    private SessionListFilter sessionFilter;
    public IReadOnlyList<SessionListFilter> SessionFilters { get; } = Enum.GetValues<SessionListFilter>();
    public SessionListFilter SessionFilter
    {
        get => sessionFilter;
        set
        {
            if (!CanRead || !Enum.IsDefined(value)) { return; }
            var searching = listSearchMode;
            InvalidateListSearch();
            sessionFilter = value;
            if (searching) { status = ListQueryHint; }
            if (selected is not null && !Sessions.Contains(selected)) { ClearSelection(); }
            OnPropertyChanged();
            Notify();
        }
    }
    public IReadOnlyList<SessionWorkspaceEntry> Sessions => sessions is null ? []
        : [.. sessions.Records.Where(record => sessionFilter == SessionListFilter.All
            || record.Authority.IsActive == (sessionFilter == SessionListFilter.Active))];
    public SessionWorkspaceEntry? SelectedSessionRecord => selected;
    internal bool ReferencesSession(HostId<SessionIdentity> session) =>
        !closed && (selected?.Authority.SessionId == session || workSnapshot?.Session.Authority.SessionId == session
        || string.Equals(historySessionId, session.Value.ToString("D"), StringComparison.Ordinal));

    internal void RevokeSessionList()
    {
        InvalidateListSearch();
        sessions = null;
        status = "Session metadata source/lifecycle changed. Rows and search continuation revoked; Refresh or Search requires fresh private admission.";
        Notify();
    }

    public IReadOnlyList<HostTaskRecord> TaskRecords => tasks?.Records ?? [];
    public bool CanInspectTask => CanRead && selectedTask is not null;
    public bool CanCancelTask => CanInspectTask && inspectedTask is
    { CurrentSource: true, Task.State: HostTaskState.IntentRecorded, Question.Status: QuestionStatus.Pending }
        && inspectedTask.Task.Request.TaskId == selectedTask?.Request.TaskId
        && inspectedTask.Task.Request.SessionId == selected?.Authority.SessionId
        && inspectedTask.Question.ExpiresAt > (workSnapshot?.ObservedAt ?? DateTimeOffset.MaxValue);

    public void SelectTask(HostTaskRecord? task)
    {
        if (!CanRead || (task is not null && !TaskRecords.Contains(task))) { return; }
        selectedTask = task;
        selectionEpoch++;
        inspectedTask = null;
        Notify();
    }

    public Task InspectTaskAsync() => RunAsync(async () =>
    {
        var epoch = selectionEpoch;
        var subject = RequireSelected().Authority.SessionId;
        var task = selectedTask?.Request.TaskId ?? throw new InvalidOperationException("Select an exact task from this page.");
        var result = await service.ExecuteCommandAsync(new(SessionCommandOperation.TaskInspect,
            subject.Value)
        { TaskId = task.Value },
            RequestOrigin.LocalUi, () => !closed && access.CanInspect && selectionEpoch == epoch
                && selected?.Authority.SessionId == subject && selectedTask?.Request.TaskId == task, lifetime.Token);
        inspectedTask = result.TaskDetails.SingleOrDefault();
        detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(result));
        status = "Exact task observation. Inspect is passive; cancellation is a separate deliberate action using these revisions.";
    });

    public Task CancelTaskAsync() => RunAsync(async () =>
    {
        var target = inspectedTask ?? throw new InvalidOperationException("Inspect the exact selected task before cancellation.");
        var question = target.Question ?? throw new InvalidOperationException("No admitted question wait exists.");
        var epoch = selectionEpoch;
        var result = await service.ExecuteCommandAsync(new(SessionCommandOperation.TaskCancel, target.Task.Request.SessionId.Value,
            target.Generation.Value)
        {
            TaskId = target.Task.Request.TaskId.Value,
            TaskRevision = target.Task.Revision.Value,
            QuestionId = question.Key.QuestionId.Value,
            QuestionRevision = question.Key.Revision.Value
        },
            RequestOrigin.LocalUi, () => !closed && access.CanInspect && selectionEpoch == epoch
                && selectedTask?.Request.TaskId == target.Task.Request.TaskId
                && selected?.Authority.SessionId == target.Task.Request.SessionId, lifetime.Token);
        await RefreshSelectedWorkAsync();
        var cancelled = result.TaskDetails.Single();
        inspectedTask = cancelled;
        detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(new("committed",
            "Exact local-version wait cancelled before dispatch; no effect termination claimed.")
        { TaskDetails = [cancelled] }));
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
        set { historySessionId = value; ClearHistorySearch(); ClearRetention(); history = null; selectedHistory = null; selectionEpoch++; OnPropertyChanged(); Notify(); }
    }
    public IReadOnlyList<SessionHistoryEvent> HistoryRecords => history?.Records ?? [];
    public SessionHistoryEvent? SelectedHistoryRecord => selectedHistory;
    public bool CanOpenHistoryDetail => CanRead && openHistoryDetail is not null && selectedHistory is not null
        && selectedHistory.SessionId == selected?.Authority.SessionId
        && history?.SessionId == selected.Authority.SessionId;

    public void SelectHistoryRecord(SessionHistoryEvent? record)
    {
        if (!CanRead || notifying || record is not null && !HistoryRecords.Contains(record)) { return; }
        selectedHistory = record;
        selectionEpoch++;
        Notify();
    }

    public Task OpenHistoryDetailAsync() => RunAsync(async () =>
    {
        var subject = RequireSelected().Authority.SessionId;
        var record = selectedHistory ?? throw new InvalidOperationException("Select an exact retained history receipt.");
        if (record.SessionId != subject || history?.SessionId != subject)
        {
            throw new InvalidOperationException("History details require the exact selected session, not another history subject.");
        }
        var present = openHistoryDetail ?? throw new InvalidOperationException("The native history detail viewer is unavailable.");
        var epoch = selectionEpoch;
        var content = await service.ReadHistoryDetailAsync(subject, record.Id, lifetime.Token);
        if (closed || !access.CanInspect || selectionEpoch != epoch || selected?.Authority.SessionId != subject
            || selectedHistory?.Id != record.Id)
        {
            throw new OperationCanceledException("The selected history receipt or private presentation changed.");
        }
        status = present(content);
    }, passive: true);
    public bool CanHistory => CanRead && Guid.TryParseExact(historySessionId, "D", out var id) && id != Guid.Empty;
    public bool CanNextHistory => CanHistory && history?.Next is not null;

    public Task ReadHistoryAsync(bool next = false) => RunAsync(async () =>
    {
        var subjectText = historySessionId;
        if (!Guid.TryParseExact(subjectText, "D", out var id) || id == Guid.Empty)
        {
            throw new InvalidOperationException("Enter the exact immutable session ID, not a name.");
        }
        var epoch = selectionEpoch;
        ClearHistorySearch();
        selectedHistory = null;
        var page = await service.ReadHistoryAsync(new(id),
            next ? history?.Next ?? throw new InvalidOperationException("No next history page.") : null, 25, lifetime.Token);
        if (closed || selectionEpoch != epoch || !string.Equals(historySessionId, subjectText, StringComparison.Ordinal))
        {
            throw new OperationCanceledException("The history subject or selected session changed.");
        }
        history = page;
        detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(new("observed", SessionHistoryPage.Scope) { History = history }));
        status = "Passive ordered history for exact ID " + id.ToString("D")
            + (history.Disposed ? ". Disposed: content redacted; immutable citations retained." : ". No session activity, reply, focus or voice target changed.");
    }, passive: true);
    public bool CanRead => !busy && !closed && access.CanInspect;
    public bool CanNext => CanRead && !listSearchMode && sessions?.Next is not null;
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
        revokeHistoryDetails?.Invoke(preview.Session.Authority.SessionId);
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
        LeaveListSearch();
        var retained = selected?.Authority.SessionId;
        sessions = await service.ReadMetadataAsync(null, 25, lifetime.Token);
        if (retained is { } id && Sessions.Any(record => record.Authority.SessionId == id))
        {
            await RefreshSelectedWorkAsync();
        }
        else { ClearSelection(); }
        status = "Active/Done minimal authority: " + sessions.Records.Length.ToString(CultureInfo.InvariantCulture)
            + " records on this page. " + (sessions.Next is null ? "End of current list." : "More pages available.");
    });

    public Task NextAsync() => RunAsync(async () =>
    {
        if (listSearchMode) { throw new InvalidOperationException("Use Next metadata search or Refresh the ordinary list."); }
        sessions = await service.ReadMetadataAsync(sessions?.Next
            ?? throw new InvalidOperationException("Refresh before requesting a next page."), 25, lifetime.Token);
        ClearSelection();
        status = "Next bounded authority page. Refresh for concurrent additions/changes.";
    });

    public Task SelectAsync(SessionWorkspaceEntry? record)
    {
        if (notifying) { return Task.CompletedTask; }
        if (busy)
        {
            if (record is not null && record.Authority.SessionId != selected?.Authority.SessionId) { selectionEpoch++; }
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
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
            await RefreshSelectedWorkAsync();
            await ReadSelectedRetentionAsync();
            Render();
            status = "Passive selected work snapshot. Questions remain exactly bound; no activity, priority, voice target or dispatch changed.";
        });
    }

    public Task NextQuestionsAsync() => RunAsync(async () =>
    {
        questions = await service.ReadQuestionsAsync(RequireSelected().Authority.SessionId,
            questions?.Next ?? throw new InvalidOperationException("No next question page."), 25, lifetime.Token);
        Render();
    });

    public Task NextTasksAsync() => RunAsync(async () =>
    {
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
        await RefreshSelectedWorkAsync();
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
            .AppendLine("Name is user content, never authority. Work snapshot, history and evidence are separate; no restored context.")
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

    private async Task RunAsync(Func<Task> operation, bool passive = false,
        HostId<SessionIdentity>? activitySession = null)
    {
        if (busy || closed || passive && refreshingWork) { return; }
        var subject = selected?.Authority.SessionId;
        var epoch = selectionEpoch;
        var clearedPresentation = false;
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), activitySession ?? viewer.SessionId,
            viewer.TaskId, RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        if (passive) { refreshingWork = true; }
        else { busy = true; Notify(); }
        try
        {
            if (!access.CanInspect) { throw new InvalidOperationException("Private desktop ownership is unavailable."); }
            await operation();
            if (closed || !access.CanInspect) { throw new OperationCanceledException("Private presentation closed."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            if (!passive || closed || !access.CanInspect
                || !busy && selected?.Authority.SessionId == subject && selectionEpoch == epoch)
            {
                sessions = null;
                RevokeListSearchPresentation();
                ClearSelection();
                clearedPresentation = true;
                status = "Cancelled or privacy closed; no late content or rollback of a possible committed lifecycle is claimed. Refresh durable state before retrying.";
            }
            else { status = "Passive observation cancelled after selection changed; no late details opened. Retry the exact selected receipt."; }
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!passive || !busy && selected?.Authority.SessionId == subject)
            {
                sessions = null;
                RevokeListSearchPresentation();
                ClearSelection();
                clearedPresentation = true;
                status = "Sessions unavailable/denied: " + exception.Message
                    + " Refresh durable state before retrying; no empty success, abandoned work or replay is claimed.";
            }
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            if (passive)
            {
                refreshingWork = false;
                if (clearedPresentation) { Notify(); }
                NotifyWork();
                NotifyQueue();
                OnPropertyChanged(nameof(CanInspectTask));
                OnPropertyChanged(nameof(CanCancelTask));
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(Detail));
                NotifyHistory();
            }
            else { busy = false; Notify(); }
            if (closed && !busy && !refreshingWork) { lifetime.Dispose(); }
        }
    }

    private void ClearSelection()
    {
        if (selected is { } previous) { memories?.ClearSessionDrafts(previous.Authority.SessionId); }
        ClearMemories();
        ClearLocalEvents();
        ClearRetention();
        selectionEpoch++;
        workSnapshot = null;
        workRecords = [];
        selectedWork = null;
        queueSnapshot = null;
        selectedQueueEntry = null;
        dispositionPreview = null;
        selected = null;
        selectedTask = null;
        inspectedTask = null;
        questions = null;
        tasks = null;
        evidencePage = null;
        history = null;
        ClearHistorySearch();
        selectedHistory = null;
        detail = string.Empty;
        nameDraft = string.Empty;
    }

    public void Close()
    {
        if (closed) { return; }
        closed = true;
        InvalidateListSearch();
        listQuery = string.Empty;
        lifetime.Cancel();
        if (!busy && !refreshingWork) { lifetime.Dispose(); }
        sessions = null;
        ClearSelection();
        Notify();
    }

    private void Notify()
    {
        notifying = true;
        try
        {
            NotifyWork();
            NotifyRetention();
            NotifyMemories();
            NotifyQueue();
            NotifyListSearch();
            OnPropertyChanged(nameof(Sessions));
            OnPropertyChanged(nameof(SelectedSessionRecord));
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
            NotifyHistory();
        }
        finally { notifying = false; }
    }

    private void NotifyHistory()
    {
        var previous = notifying;
        notifying = true;
        try
        {
            OnPropertyChanged(nameof(HistorySessionId));
            OnPropertyChanged(nameof(CanHistory));
            OnPropertyChanged(nameof(CanNextHistory));
            OnPropertyChanged(nameof(HistoryRecords));
            OnPropertyChanged(nameof(SelectedHistoryRecord));
            OnPropertyChanged(nameof(CanOpenHistoryDetail));
            NotifyHistorySearch();
        }
        finally { notifying = previous; }
    }
}