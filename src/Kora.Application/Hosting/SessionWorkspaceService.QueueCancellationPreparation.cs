using System.Collections.Immutable;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    public async Task<SessionQueueCancellationPreparation> PrepareQueueCancellationAsync(
        SessionQueueCancellationReviewCommand command, RequestOrigin origin, Func<bool> admitted, CancellationToken token)
    {
        if (command.Error is not null || command.IsHelp || command.SessionId == Guid.Empty
            || command.TaskIds.IsDefaultOrEmpty || command.TaskIds.Length > SessionQueueLimits.MaximumPendingPerSession
            || command.TaskIds.Contains(Guid.Empty) || command.TaskIds.Distinct().Count() != command.TaskIds.Length)
        { throw new InvalidOperationException("Select an exact session and complete bounded unique pending subset."); }
        var revision = access.ControlRevision;
        bool Eligible() => admitted() && access.CanControl && access.ControlRevision == revision;
        var result = await ExecuteCommandAsync(new(SessionCommandOperation.QueueList, command.SessionId),
            origin, Eligible, token).ConfigureAwait(false);
        var preparation = FreezeQueueCancellationPreparation(command, result.Work, revision);
        token.ThrowIfCancellationRequested();
        if (!Eligible()) { throw new OperationCanceledException("Original queue preparation admission changed."); }
        return preparation;
    }

    internal static SessionQueueCancellationPreparation FreezeQueueCancellationPreparation(
        SessionQueueCancellationReviewCommand command, SessionWorkSnapshot? work, long revision)
    {
        if (work is null) { throw new InvalidDataException("The exact queue work observation is missing."); }
        work.RequireSubject(new(command.SessionId));
        if (!work.Session.Authority.IsActive || work.Queue.DispatchControl is null)
        { throw new InvalidOperationException("The exact active current-run queue control is unavailable."); }
        var requested = command.TaskIds.ToHashSet();
        var selection = work.Queue.Entries.Where(entry => requested.Contains(entry.Request.TaskId.Value))
            .Select(entry => new SessionQueueSelection(entry.Request.TaskId, entry.Revision)).ToImmutableArray();
        if (selection.Length != requested.Count)
        { throw new InvalidOperationException("The complete requested subset is missing; no remaining subset is selected."); }
        _ = SessionQueueCancellation.Select(work.Queue, selection);
        return new(work, selection, revision);
    }
}
