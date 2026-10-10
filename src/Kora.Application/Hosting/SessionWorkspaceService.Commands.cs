using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    public Task<SessionCommandResult> ExecuteCommandAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(admission);
        if (command.Operation == SessionCommandOperation.Invalid)
        {
            throw new InvalidOperationException(command.Error);
        }
        if (command.Operation is SessionCommandOperation.QueueList or SessionCommandOperation.QueueStatus
            or SessionCommandOperation.QueueEnqueue or SessionCommandOperation.QueueCancel or SessionCommandOperation.QueueRemove
            or SessionCommandOperation.QueueClear or SessionCommandOperation.QueueDispatch)
        {
            return (queue ?? throw new InvalidOperationException("The deterministic queue service is unavailable."))
                .ExecuteCommandAsync(command, origin, admission, token);
        }
        if (command.Operation is SessionCommandOperation.History or SessionCommandOperation.HistoryGet or SessionCommandOperation.HistorySearch)
        {
            return ExecuteHistoryCommandAsync(command, origin, admission, token);
        }
        var exactTask = command.Operation is SessionCommandOperation.TaskStatus
            or SessionCommandOperation.TaskInspect or SessionCommandOperation.TaskCancel;
        if (exactTask && command.SessionId is null)
        {
            throw new InvalidOperationException("An exact existing task session ID is required.");
        }
        var subject = new HostId<SessionIdentity>(command.SessionId ?? Guid.NewGuid());
        var inspection = command.Operation is SessionCommandOperation.Help or SessionCommandOperation.List
            or SessionCommandOperation.Status or SessionCommandOperation.Inspect
            or SessionCommandOperation.TaskStatus or SessionCommandOperation.TaskInspect;
        return ControlAsync(subject, origin, async (request, eligible) =>
        {
            SessionCommandResult result;
            switch (command.Operation)
            {
                case SessionCommandOperation.Help:
                    result = new("observed", SessionCommand.Syntax + " " + SessionCommand.TaskSyntax + " " + SessionCommand.QueueSyntax);
                    break;
                case SessionCommandOperation.TaskStatus:
                case SessionCommandOperation.TaskInspect:
                    var observed = await store.ReadTaskAsync(subject, new(command.TaskId
                        ?? throw new InvalidOperationException("An exact task ID is required.")), token).ConfigureAwait(false);
                    result = observed is null ? new("unknown", "No task with that exact ID belongs to the addressed existing session.")
                        : new("observed", "Authoritative durable task state; no inferred progress, steps, effects or ETA.")
                        {
                            TaskDetails = [observed],
                        };
                    break;
                case SessionCommandOperation.TaskCancel:
                    var cancelled = await store.CancelWaitingTaskAsync(request, new(subject,
                        new(command.TaskId ?? throw new InvalidOperationException("An exact task ID is required.")),
                        new(command.TaskRevision), new(command.Generation),
                        new(command.QuestionId ?? throw new InvalidOperationException("An exact question ID is required.")),
                        new(command.QuestionRevision)), eligible, token).ConfigureAwait(false);
                    result = new("committed", "Exact pre-dispatch local-version work and question cancelled atomically. No effect was dispatched or termination claimed.")
                    { TaskDetails = [cancelled] };
                    NotifyCancellation(cancelled);
                    break;
                case SessionCommandOperation.List:
                    var page = await store.ReadMetadataPageAsync(command.After, command.Limit, token).ConfigureAwait(false);
                    result = new("observed", Observation) { Sessions = [.. page.Records.Select(Project)], Next = page.Next };
                    break;
                case SessionCommandOperation.Status:
                    var entry = await store.ReadMetadataAsync(subject, token).ConfigureAwait(false);
                    result = new("observed", Observation) { Sessions = [Project(entry)] };
                    break;
                case SessionCommandOperation.Inspect:
                    var inspected = await store.ReadMetadataAsync(subject, token).ConfigureAwait(false);
                    if (command.PageKind is "tasks")
                    {
                        var taskPage = await store.ReadTaskPageAsync(subject, command.After, command.Limit, token).ConfigureAwait(false);
                        result = new("observed", Observation) { Sessions = [Project(inspected)],
                            Tasks = [.. taskPage.Records.Select(task => new SessionCommandTask(task.Request.TaskId.Value,
                                task.Request.RequestId.Value, task.State, task.Revision.Value, task.Request.Origin))], Next = taskPage.Next };
                    }
                    else if (command.PageKind is "questions")
                    {
                        var questions = await store.ReadQuestionPageAsync(subject, command.After, command.Limit, token).ConfigureAwait(false);
                        result = new("observed", Observation) { Sessions = [Project(inspected)],
                            Questions = [.. questions.Records.Select(question => new SessionCommandQuestion(
                                question.Key.QuestionId.Value, question.Key.Revision.Value, question.Status.ToString()))], Next = questions.Next };
                    }
                    else { throw new InvalidOperationException("Choose the tasks or questions page."); }
                    break;
                case SessionCommandOperation.Create:
                    var created = await store.CreateNamedSessionAsync(request, RequireName(command), eligible, token).ConfigureAwait(false);
                    result = Changed(created, "Created empty Active session. No execution, context or permission created.");
                    break;
                case SessionCommandOperation.Rename:
                    var renamed = await store.RenameSessionAsync(request, new(command.Generation), command.MetadataRevision,
                        RequireName(command), eligible, token).ConfigureAwait(false);
                    result = Changed(renamed, "Renamed exact ID. Name is content only; generation and approvals unchanged.");
                    break;
                case SessionCommandOperation.Done:
                case SessionCommandOperation.Resume:
                    var lifecycle = await store.ChangeIdleLifecycleAsync(request, new(command.Generation),
                        command.Operation == SessionCommandOperation.Resume, eligible, token).ConfigureAwait(false);
                    SessionLifecycleChanged?.Invoke(subject);
                    result = new("committed", "Lifecycle committed for exact ID. No tasks or approvals replayed; refresh metadata.")
                    {
                        Sessions = [new(lifecycle.SessionId.Value, lifecycle.IsActive, lifecycle.Generation.Value, null, null)],
                    };
                    break;
                default:
                    throw new InvalidOperationException("Unsupported session command.");
            }
            if (inspection)
            {
                token.ThrowIfCancellationRequested();
                if (!eligible()) { throw new InvalidOperationException("Session admission changed. Refresh and initiate a new command."); }
            }
            _ = SessionCommandResult.Serialize(result);
            return result;
        }, token, admission, inspection || command.Operation == SessionCommandOperation.TaskCancel, existingSubject: exactTask);
    }

    private const string Observation =
        "Bounded durable observation, not an atomic runtime ledger. Names are labels only. "
        + "No conversation, queue, scheduler, approval targeting or model context. Refresh for concurrent changes.";

    private async Task<SessionCommandResult> ExecuteHistoryCommandAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admission, CancellationToken token)
    {
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || command.SessionId is null
            || !admission())
        {
            throw new InvalidOperationException("History requires deliberate trusted input and an exact session ID.");
        }
        var revision = access.ControlRevision;
        var session = new HostId<SessionIdentity>(command.SessionId.Value);
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Request);
        try
        {
            var result = command.Operation == SessionCommandOperation.HistorySearch
                ? new SessionCommandResult("observed", SessionHistorySearchPage.Scope)
                {
                    HistorySearch = await SearchHistoryAsync(session, command.HistoryQuery
                        ?? throw new InvalidOperationException("A lexical query is required."),
                        command.HistorySearchCursor, command.Limit, token).ConfigureAwait(false),
                }
                : command.Operation == SessionCommandOperation.History
                ? new SessionCommandResult("observed", SessionHistoryPage.Scope)
                {
                    History = await ReadHistoryAsync(session, command.HistoryCursor, command.Limit, token).ConfigureAwait(false),
                }
                : new SessionCommandResult("observed", SessionHistoryPage.Scope)
                {
                    HistoryEvent = await ReadHistoryEventAsync(session, command.HistoryEventId
                        ?? throw new InvalidOperationException("An exact history event ID is required."), token).ConfigureAwait(false),
                };
            if (command.Operation == SessionCommandOperation.HistoryGet && result.HistoryEvent is null)
            {
                result = result with { Outcome = "unknown", Message = "No history event with that exact ID belongs to the addressed session." };
            }
            token.ThrowIfCancellationRequested();
            if (!admission() || !access.CanInspect || access.ControlRevision != revision)
            {
                throw new InvalidOperationException("History admission changed; no content may be presented.");
            }
            _ = SessionCommandResult.Serialize(result);
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private static SessionName RequireName(SessionCommand command) =>
        command.Name ?? throw new InvalidOperationException("A validated quoted name is required.");

    private static SessionCommandSession Project(SessionWorkspaceEntry entry) =>
        new(entry.Authority.SessionId.Value, entry.Authority.IsActive, entry.Authority.Generation.Value,
            entry.Metadata?.Revision.Value ?? 0, entry.Metadata?.Name.Value);

    private static SessionCommandResult Changed(SessionWorkspaceEntry entry, string message) =>
        new("committed", message) { Sessions = [Project(entry)] };
}
