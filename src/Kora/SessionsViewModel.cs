using System.Globalization;
using System.Text;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Core.Authorization;
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
    private SessionPage<WorkSessionAuthorization>? sessions;
    private SessionPage<HostQuestionRecord>? questions;
    private SessionPage<HostTaskRecord>? tasks;
    private EvidencePage? evidencePage;
    private WorkSessionAuthorization? selected;
    private string status = "Refresh to inspect existing minimal durable authority. No conversation or queue store is available.";
    private string detail = string.Empty;
    private bool busy;
    private bool closed;

    public IReadOnlyList<WorkSessionAuthorization> Sessions => sessions?.Records ?? [];
    public string Status => status;
    public string Detail => detail;
    public bool CanRead => !busy && !closed && access.CanInspect;
    public bool CanNext => CanRead && sessions?.Next is not null;
    public bool CanNextQuestions => CanRead && questions?.Next is not null;
    public bool CanNextTasks => CanRead && tasks?.Next is not null;
    public bool CanEvidence => CanRead && selected is not null;
    public bool CanNextEvidence => CanEvidence && evidencePage?.Cursor is not null;
    public bool CanDone => CanRead && access.CanControl && selected?.IsActive == true;
    public bool CanResume => CanRead && access.CanControl && selected?.IsActive == false;

    public Task RefreshAsync() => RunAsync(async () =>
    {
        sessions = await service.ReadSessionsAsync(null, 25, lifetime.Token);
        ClearSelection();
        status = "Active/Done minimal authority: " + sessions.Records.Length.ToString(CultureInfo.InvariantCulture)
            + " records on this page. " + (sessions.Next is null ? "End of current list." : "More pages available.");
    });

    public Task NextAsync() => RunAsync(async () =>
    {
        sessions = await service.ReadSessionsAsync(sessions?.Next
            ?? throw new InvalidOperationException("Refresh before requesting a next page."), 25, lifetime.Token);
        ClearSelection();
        status = "Next bounded authority page. Refresh for concurrent additions/changes.";
    });

    public Task SelectAsync(WorkSessionAuthorization? record) => RunAsync(async () =>
    {
        if (record is not null && !Sessions.Contains(record))
        {
            throw new InvalidOperationException("Select an existing record from this page.");
        }
        ClearSelection();
        selected = record;
        if (record is null) { return; }
        questions = await service.ReadQuestionsAsync(record.SessionId, null, 25, lifetime.Token);
        tasks = await service.ReadTasksAsync(record.SessionId, null, 25, lifetime.Token);
        Render();
        status = "Passive selected detail. Selection never targets a question, approval or command. Pages are observations, not an atomic work ledger.";
    });

    public Task NextQuestionsAsync() => RunAsync(async () =>
    {
        questions = await service.ReadQuestionsAsync(RequireSelected().SessionId,
            questions?.Next ?? throw new InvalidOperationException("No next question page."), 25, lifetime.Token);
        Render();
    });

    public Task NextTasksAsync() => RunAsync(async () =>
    {
        tasks = await service.ReadTasksAsync(RequireSelected().SessionId,
            tasks?.Next ?? throw new InvalidOperationException("No next task page."), 25, lifetime.Token);
        Render();
    });

    public Task ReadEvidenceAsync(bool next = false) => RunAsync(async () =>
    {
        evidencePage = await evidence.QueryAsync(new() { SessionId = RequireSelected().SessionId },
            next ? evidencePage?.Cursor ?? throw new InvalidOperationException("No next evidence page.") : null, lifetime.Token);
        Render();
    });

    public Task ChangeLifecycleAsync(bool active) => RunAsync(async () =>
    {
        var target = RequireSelected();
        var changed = await service.ChangeLifecycleAsync(target.SessionId, target.Generation, active,
            RequestOrigin.LocalUi, lifetime.Token);
        selected = changed;
        questions = await service.ReadQuestionsAsync(changed.SessionId, null, 25, lifetime.Token);
        tasks = await service.ReadTasksAsync(changed.SessionId, null, 25, lifetime.Token);
        evidencePage = null;
        Render();
        status = "Committed " + (active ? "resume" : "Done") + " for " + changed.SessionId.Value.ToString("D")
            + ". Generation advanced. No task, approval or context replay. Refresh the list to observe the new state.";
    });

    private WorkSessionAuthorization RequireSelected() =>
        selected ?? throw new InvalidOperationException("Select the exact subject session first.");

    private void Render()
    {
        var record = RequireSelected();
        var text = new StringBuilder().Append("Session ").Append(record.SessionId.Value.ToString("D"))
            .Append(record.IsActive ? " | Active" : " | Done").Append(" | generation ").Append(record.Generation.Value)
            .AppendLine().AppendLine("Minimal authority only; no title, conversation, queue, scheduler or restored context.")
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
        selected = null;
        questions = null;
        tasks = null;
        evidencePage = null;
        detail = string.Empty;
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
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(CanRead));
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanNextQuestions));
        OnPropertyChanged(nameof(CanNextTasks));
        OnPropertyChanged(nameof(CanEvidence));
        OnPropertyChanged(nameof(CanNextEvidence));
        OnPropertyChanged(nameof(CanDone));
        OnPropertyChanged(nameof(CanResume));
    }

    [LoggerMessage(313, LogLevel.Error, "Native Sessions workspace failed; exception type {ExceptionType}.")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
