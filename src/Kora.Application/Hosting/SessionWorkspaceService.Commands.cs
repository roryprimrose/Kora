using Kora.Core.Commands;
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
        var subject = new HostId<SessionIdentity>(command.SessionId ?? Guid.NewGuid());
        var inspection = command.Operation is SessionCommandOperation.Help or SessionCommandOperation.List
            or SessionCommandOperation.Status or SessionCommandOperation.Inspect;
        return ControlAsync(subject, origin, async (request, eligible) =>
        {
            SessionCommandResult result;
            switch (command.Operation)
            {
                case SessionCommandOperation.Help:
                    result = new("observed", SessionCommand.Syntax);
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
        }, token, admission, inspection);
    }

    private const string Observation =
        "Bounded durable observation, not an atomic runtime ledger. Names are labels only. "
        + "No conversation, queue, scheduler, approval targeting or model context. Refresh for concurrent changes.";

    private static SessionName RequireName(SessionCommand command) =>
        command.Name ?? throw new InvalidOperationException("A validated quoted name is required.");

    private static SessionCommandSession Project(SessionWorkspaceEntry entry) =>
        new(entry.Authority.SessionId.Value, entry.Authority.IsActive, entry.Authority.Generation.Value,
            entry.Metadata?.Revision.Value ?? 0, entry.Metadata?.Name.Value);

    private static SessionCommandResult Changed(SessionWorkspaceEntry entry, string message) =>
        new("committed", message) { Sessions = [Project(entry)] };
}
