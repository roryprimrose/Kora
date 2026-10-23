using System.Diagnostics;
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
    Kora.Application.Memory.MemoryManagementService? memories = null,
    SessionAttachmentWindowController? attachments = null,
    Kora.Application.Configuration.SessionProviderProfileConfigurationService? providers = null,
    SessionConversationService? conversation = null,
    Func<AdmittedDetailContent, Func<bool>, Func<CancellationToken, Task<AdmittedDetailContent>>, CancellationToken, Task<string>>? openConversationDetail = null,
    Action<HostId<SessionIdentity>>? revokeConversationDetails = null,
    Func<SessionWorkspaceEntry, Kora.Core.Conversation.ConversationMessage, Func<bool>, CancellationToken, Task<string>>? reviewOriginalMessageReuse = null,
    Func<Kora.Core.Conversation.ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken,
        Task<Kora.Application.Configuration.AppearanceSuggestionReview>>? reviewAppearanceSuggestion = null,
    Func<Kora.Application.Configuration.AppearanceSuggestionReview, CancellationToken,
        Task<Kora.Application.Configuration.AppearanceApplyResult>>? confirmAppearanceSuggestion = null,
    ScopedInstructionUseBroker? instructionUse = null,
    SessionAnswerReadWorkflow? answerReads = null) : ObservableObject
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

    public bool CanAttachFile => CanRead && selected is { Authority.IsActive: true } && attachments is not null;
    public bool CanInspectAttachment => CanRead && selected is not null && attachments is not null;
    public Task OpenAttachment(bool attach) => RunAsync(async () =>
    {
        var target = RequireSelected();
        var epoch = selectionEpoch;
        await (attachments ?? throw new InvalidOperationException("The native attachment surface is unavailable."))
            .Open(target, attach, () => !closed && access.CanInspect && selectionEpoch == epoch
                && selected?.Authority == target.Authority, lifetime.Token);
        status = attach ? "Review exact metadata and separate durable-copy disclosure before confirming."
            : "Exact retained historical attachment. Browsing never resumes a Done session or renews activity.";
    });

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
    public IReadOnlyList<SessionWorkspaceEntry> Sessions => listSearchMode && listSearch is not null
        && sessions?.Records == listSearch.Records ? orderedListPage
        : sessions is null ? []
        : [.. sessions.Records.Where(record => sessionFilter == SessionListFilter.All
            || record.Authority.IsActive == (sessionFilter == SessionListFilter.Active))];
    public SessionWorkspaceEntry? SelectedSessionRecord => selected;
    internal Func<bool> CaptureNavigationEligibility()
    {
        var epoch = selectionEpoch;
        return () => !closed && selectionEpoch == epoch;
    }
    internal Func<bool> CaptureDraftPresentationEligibility()
    {
        var source = CaptureNavigationEligibility();
        var control = access.ControlRevision;
        var target = selected;
        return () => source() && access.CanInspect && access.CanControl && access.ControlRevision == control && selected == target;
    }
    internal bool ReferencesSession(HostId<SessionIdentity> session) =>
        !closed && (selected?.Authority.SessionId == session || workSnapshot?.Session.Authority.SessionId == session
        || string.Equals(historySessionId, session.Value.ToString("D"), StringComparison.Ordinal));

    internal void RevokeSessionList()
    {
        ClearLocalEvents();
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
        Detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(result));
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
        Detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(new("committed",
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
    public string Detail
    {
        get => eventDetailPresented && !EventSourceCurrent ? string.Empty : detail;
        private set { ClearDispositionPreview(); detail = value; eventDetailPresented = false; }
    }
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
        Detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(new("observed", SessionHistoryPage.Scope) { History = history }));
        status = "Passive ordered history for exact ID " + id.ToString("D")
            + (history.Disposed ? ". Disposed: content redacted; immutable citations retained." : ". No session activity, reply, focus or voice target changed.");
    }, passive: true);
    public bool CanRead => !busy && !protectingComposer && !closed && access.CanInspect;
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
    public bool CanConfirmDisposition => CanPreviewDisposition && dispositionPreview is { } preview
        && selected == preview.Session && service.IsDispositionPreviewCurrent(preview) && dispositionNativeAdmission();

    private Func<bool> dispositionNativeAdmission = () => true;

    public Task PreviewDispositionAsync() => RunAsync(async () =>
    {
        if (!await ProtectDispositionComposerAsync()) { return; }
        var target = RequireSelected();
        await PrepareDispositionAsync(target.Authority.SessionId, target.Authority.Generation,
            target.Metadata?.Revision.Value ?? 0, () => true, () => true, lifetime.Token);
    });

    internal async Task<bool> ReviewDispositionAsync(SessionDispositionReviewCommand command, RequestOrigin origin,
        Func<bool> admitted, Func<bool> nativeAdmission, CancellationToken token)
    {
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || command.Error is not null)
        { throw new InvalidOperationException("Disposition review requires an original user preview request."); }
        if (!CanRead)
        { throw new InvalidOperationException("Sessions is busy or unavailable; finish the native operation and retry the exact disposition review when idle."); }
        var displayed = false;
        ActivityLink[] causes = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
        await RunAsync(async () =>
        {
            if (!await ProtectDispositionComposerAsync()) { return; }
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
            await PrepareDispositionAsync(new(command.SessionId), new(command.Generation), command.MetadataRevision,
                admitted, nativeAdmission, cancellation.Token);
            displayed = true;
        }, activitySession: new(command.SessionId), origin: origin, activityLinks: causes);
        return displayed && CanConfirmDisposition;
    }

    private async Task<bool> ProtectDispositionComposerAsync()
    {
        ClearDispositionPreview();
        try { return await ProtectComposerAsync(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportComposerCloseFailure(exception);
            status = "Disposition review not opened: original input admission changed. Unsent messages remain held; inspect the exact source before retrying.";
            return false;
        }
    }

    private async Task PrepareDispositionAsync(HostId<SessionIdentity> session, HostRevision generation,
        long metadataRevision, Func<bool> admitted, Func<bool> nativeAdmission, CancellationToken token)
    {
        ClearDispositionPreview();
        var epoch = selectionEpoch;
        var revision = access.ControlRevision;
        token.ThrowIfCancellationRequested();
        if (!admitted() || !nativeAdmission()) { throw new OperationCanceledException("Exact review admission changed."); }
        var preview = await service.PreviewDispositionAsync(session, generation, metadataRevision, token);
        if (closed || token.IsCancellationRequested || !access.CanControl || revision != access.ControlRevision
            || epoch != selectionEpoch || !admitted() || !nativeAdmission() || !service.IsDispositionPreviewCurrent(preview))
        {
            service.DiscardDispositionPreview(preview);
            throw new OperationCanceledException("Exact review changed or closed. Request a fresh preview.");
        }
        if (selected != preview.Session)
        {
            ClearSelection();
            selected = preview.Session;
            sessions = new([preview.Session], null);
        }
        Detail = "Exact session ID: " + preview.Session.Authority.SessionId.Value.ToString("D")
            + "\nName (label only): " + (preview.Session.Metadata?.Name.Value ?? "Unnamed")
            + "\nGeneration: " + preview.Session.Authority.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + "\nMetadata revision: " + (preview.Session.Metadata?.Revision.Value ?? 0).ToString(CultureInfo.InvariantCulture)
            + "\nLive rows to remove: questions " + preview.Questions.ToString(CultureInfo.InvariantCulture)
            + ", scoped grants " + preview.ScopedGrants.ToString(CultureInfo.InvariantCulture)
            + ", observations " + preview.Observations.ToString(CultureInfo.InvariantCulture)
            + ", waits " + preview.Waits.ToString(CultureInfo.InvariantCulture)
            + ", durable file attachments " + preview.Attachments.ToString(CultureInfo.InvariantCulture)
            + ", provider overrides " + preview.ProviderProfiles.ToString(CultureInfo.InvariantCulture)
            + ", conversation messages " + preview.ConversationMessages.ToString(CultureInfo.InvariantCulture)
            + ", saved conversation drafts " + preview.SavedConversationDrafts.ToString(CultureInfo.InvariantCulture)
            + "\n" + SessionDispositionPreview.Scope;
        dispositionPreview = preview;
        dispositionNativeAdmission = nativeAdmission;
        status = "Preview only; nothing removed. Confirm logical disposition is a separate deliberate action for this exact ID and revision.";
    }

    internal void RevokeDispositionReview()
    {
        if (dispositionPreview is null) { return; }
        ClearDispositionPreview();
        Detail = string.Empty;
        status = "Disposition review owner closed or hidden. Request a fresh exact preview; nothing was removed.";
        Notify();
    }

    private void ClearDispositionPreview()
    {
        if (dispositionPreview is { } preview) { service.DiscardDispositionPreview(preview); }
        dispositionPreview = null;
        dispositionNativeAdmission = () => true;
    }

    public Task ConfirmDispositionAsync() => RunAsync(async () =>
    {
        if (!await ProtectComposerAsync()) { return; }
        var preview = dispositionPreview ?? throw new InvalidOperationException("Preview the exact session first.");
        var nativeAdmission = dispositionNativeAdmission;
        dispositionPreview = null;
        revokeHistoryDetails?.Invoke(preview.Session.Authority.SessionId);
        var receipt = await service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi,
            () => !closed && access.CanControl && nativeAdmission(), lifetime.Token);
        RetireUnsent(preview.Session.Authority.SessionId);
        sessions = null;
        ClearSelection();
        Detail = SessionDispositionPreview.Scope;
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
        else if (await ProtectComposerAsync()) { ClearSelection(); }
        status = "Active/Done minimal authority: " + sessions.Records.Length.ToString(CultureInfo.InvariantCulture)
            + " records on this page. " + (sessions.Next is null ? "End of current list." : "More pages available.");
    });

    public Task NextAsync() => RunAsync(async () =>
    {
        if (!await ProtectComposerAsync()) { return; }
        if (listSearchMode) { throw new InvalidOperationException("Use Next metadata search or Refresh the ordinary list."); }
        sessions = await service.ReadMetadataAsync(sessions?.Next
            ?? throw new InvalidOperationException("Refresh before requesting a next page."), 25, lifetime.Token);
        ClearSelection();
        status = "Next bounded authority page. Refresh for concurrent additions/changes.";
    });

    public Task SelectAsync(SessionWorkspaceEntry? record)
    {
        if (notifying) { return Task.CompletedTask; }
        if (protectingComposer) { Notify(); return Task.CompletedTask; }
        if (record == selected) { return Task.CompletedTask; }
        if (busy)
        {
            if (record is not null && record.Authority.SessionId != selected?.Authority.SessionId) { selectionEpoch++; }
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
        {
            var candidate = record is null ? null : Sessions.FirstOrDefault(row => row == record)
                ?? throw new InvalidOperationException("Select an existing record from this page.");
            var source = sessions;
            var sourceEpoch = listSearchEpoch;
            var admission = access.ControlRevision;
            if (!await ProtectComposerAsync()) { return; }
            if (closed || !access.CanInspect || admission != access.ControlRevision
                || sourceEpoch != listSearchEpoch || !ReferenceEquals(source, sessions)
                || candidate is not null && !Sessions.Any(row => ReferenceEquals(row, candidate)))
            { throw new OperationCanceledException("The exact session row or source changed during navigation review."); }
            await ReadSelectedAsync(candidate);
        });
    }

    internal Task SelectExactAsync(HostId<SessionIdentity> session, Func<bool> admitted, CancellationToken token) =>
        RunAsync(async () =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
            if (!admitted()) { throw new OperationCanceledException("The overview source changed."); }
            if (!await ProtectComposerAsync()) { return; }
            var page = await service.SearchListAsync(SessionListSearchKind.ExactId, SessionListFilter.All,
                session.Value.ToString("D"), null, 1, cancellation.Token);
            var record = page.Records.Single();
            var observedAdmission = access.ControlRevision;
            var snapshot = await service.ReadWorkAsync(session, cancellation.Token);
            var observedQuestions = await service.ReadQuestionsAsync(session, null, 25, cancellation.Token);
            var observedTasks = await service.ReadTasksAsync(session, null, 25, cancellation.Token);
            var observedRetention = await service.ReadRetentionAsync(session, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!admitted() || snapshot.Session != record || observedRetention.Generation != record.Authority.Generation)
            {
                throw new OperationCanceledException("The overview source, exact metadata or generation changed.");
            }
            LeaveListSearch();
            ClearSelection();
            sessionFilter = SessionListFilter.All;
            sessions = new(page.Records, null);
            selected = record;
            nameDraft = record.Metadata?.Name.Value ?? string.Empty;
            historySessionId = session.Value.ToString("D");
            questions = observedQuestions;
            tasks = observedTasks;
            retentionSubject = session;
            retentionObservation = observedRetention;
            ApplyWork(snapshot, observedAdmission);
            Render();
            status = "Fresh passive exact-session navigation. Opening and selection grant nothing; answer only in the original Questions window.";
            // Navigation cannot renew an inspected task cancellation target, even when a question wait was observed.
            inspectedTask = null;
            selectedTask = null;
            selectedQueueEntry = null;
            selectedWork = null;
            OnPropertyChanged(nameof(SessionFilter));
        }, activitySession: session);

    private async Task ReadSelectedAsync(SessionWorkspaceEntry? record)
    {
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
        if (!await ProtectComposerAsync()) { return; }
        var target = RequireSelected();
        var changed = await service.ChangeLifecycleAsync(target.Authority.SessionId, target.Authority.Generation, active,
            RequestOrigin.LocalUi, lifetime.Token);
        RetireUnsent(target.Authority.SessionId);
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
        if (!await ProtectComposerAsync()) { return; }
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
        Detail = text.ToString();
    }

    private async Task RunAsync(Func<Task> operation, bool passive = false,
        HostId<SessionIdentity>? activitySession = null, bool routineNoticeControl = false,
        bool dispatchControl = false, RequestOrigin origin = RequestOrigin.LocalUi,
        IEnumerable<ActivityLink>? activityLinks = null)
    {
        if (busy || closed || passive && refreshingWork
            || routineNoticeControl && changingRoutineQuiet || dispatchControl && changingQueueControl) { return; }
        var subject = selected?.Authority.SessionId;
        var epoch = selectionEpoch;
        var clearedPresentation = false;
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), activitySession ?? viewer.SessionId,
            viewer.TaskId, origin), HostActivityLayer.Desktop, HostOperation.Request, activityLinks);
        if (dispatchControl) { changingQueueControl = true; NotifyQueue(); }
        else if (routineNoticeControl) { changingRoutineQuiet = true; NotifyLocalEvents(); }
        else if (passive) { refreshingWork = true; }
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
            if (dispatchControl && !closed && access.CanInspect
                && (selected?.Authority.SessionId != subject || selectionEpoch != epoch))
            {
                activity.Complete(HostOperationOutcome.Cancelled);
            }
            else if (dispatchControl && !closed && access.CanInspect)
            {
                queueSnapshot = null;
                status = "Queue admission control cancelled/unconfirmed. Refresh exact work and control receipts; no rollback or unpause is inferred.";
            }
            else if (routineNoticeControl && !closed && access.CanInspect)
            {
                ClearLocalEvents();
                status = "Routine quiet control cancelled/unconfirmed. Refresh exact notice recovery; no rollback, default or backlog replay is claimed. Authoritative work remains independent.";
            }
            else if (!passive || closed || !access.CanInspect
                || !busy && selected?.Authority.SessionId == subject && selectionEpoch == epoch)
            {
                sessions = null;
                RevokeListSearchPresentation();
                conversationSourceHeld = true;
                ClearSelection();
                clearedPresentation = true;
                status = "Cancelled or privacy closed; no late content or rollback of a possible committed lifecycle is claimed. Refresh durable state before retrying.";
            }
            else { status = "Passive observation cancelled after selection changed; no late details opened. Retry the exact selected receipt."; }
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (dispatchControl && !closed && access.CanInspect
                && (selected?.Authority.SessionId != subject || selectionEpoch != epoch))
            {
                activity.Complete(HostOperationOutcome.Failed);
            }
            else if (dispatchControl && !closed && access.CanInspect)
            {
                queueSnapshot = null;
                status = "Queue admission control unavailable: " + exception.GetType().Name
                    + ". Refresh exact work and control receipts; no rollback, unpause or termination is inferred.";
            }
            else if (routineNoticeControl)
            {
                ClearLocalEvents();
                eventUnavailable = "Routine quiet control held/unavailable: " + exception.GetType().Name + ". No confirmed choice is inferred; restart/refresh recovery and independent work remain available.";
                status = eventUnavailable;
            }
            else if (!passive || !busy && selected?.Authority.SessionId == subject)
            {
                sessions = null;
                RevokeListSearchPresentation();
                conversationSourceHeld = true;
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
            if (dispatchControl)
            {
                changingQueueControl = false;
                NotifyQueue();
                NotifyWork();
                NotifyLocalEvents();
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(Detail));
            }
            else if (routineNoticeControl)
            {
                changingRoutineQuiet = false;
                NotifyLocalEvents();
                OnPropertyChanged(nameof(Status));
            }
            else if (passive)
            {
                refreshingWork = false;
                if (clearedPresentation) { Notify(); }
                NotifyWork();
                NotifyQueue();
                NotifyLocalEvents();
                OnPropertyChanged(nameof(CanInspectTask));
                OnPropertyChanged(nameof(CanCancelTask));
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(Detail));
                NotifyHistory();
            }
            else { busy = false; Notify(); }
            if (closed && !busy && !refreshingWork && !changingRoutineQuiet && !changingQueueControl) { lifetime.Dispose(); }
        }
    }

    private void ClearSelection()
    {
        ClearConversation();
        ClearProviderReview();
        attachments?.Close();
        if (selected is { } previous) { memories?.ClearSessionDrafts(previous.Authority.SessionId); }
        ClearMemories();
        ClearLocalEvents();
        ClearRetention();
        selectionEpoch++;
        workSnapshot = null;
        presentingWork = true;
        try { workRecords.Clear(); }
        finally { presentingWork = false; }
        selectedWork = null;
        queueSnapshot = null;
        selectedQueueEntry = null;
        queueCancelReview = null;
        queueCancellationAdmission = static () => true;
        pendingCancellationSelection = [];
        pendingCancellationEpoch++;
        OnPropertyChanged(nameof(PendingCancellationEntries));
        ClearDispositionPreview();
        selected = null;
        selectedTask = null;
        inspectedTask = null;
        questions = null;
        tasks = null;
        evidencePage = null;
        history = null;
        ClearHistorySearch();
        selectedHistory = null;
        Detail = string.Empty;
        nameDraft = string.Empty;
    }

    public void Close()
    {
        if (closed) { return; }
        closed = true;
        ClearUnsent();
        composerBody = string.Empty;
        RevokeListSearchPresentation();
        lifetime.Cancel();
        if (!busy && !protectingComposer && !refreshingWork && !changingRoutineQuiet && !changingQueueControl) { lifetime.Dispose(); }
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
            NotifyConversation();
            NotifyProviderReview();
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
            OnPropertyChanged(nameof(CanAttachFile));
            OnPropertyChanged(nameof(CanInspectAttachment));
            NotifyHistorySearch();
        }
        finally { notifying = previous; }
    }
}