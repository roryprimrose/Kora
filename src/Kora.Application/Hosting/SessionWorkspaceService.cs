using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService(
    ISessionWorkspaceStore store, HostTaskCoordinator tasks,
    ISessionWorkspaceAccess access, ILogger<SessionWorkspaceService> logger)
{
    public Task<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) =>
        ReadAsync(() => store.ReadSessionsAsync(after, limit, token));

    public Task<SessionPage<HostQuestionRecord>> ReadQuestionsAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) =>
        ReadAsync(() => store.ReadQuestionPageAsync(session, after, limit, token));

    public Task<SessionPage<HostTaskRecord>> ReadTasksAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) =>
        ReadAsync(() => store.ReadTaskPageAsync(session, after, limit, token));

    private async Task<SessionPage<T>> ReadAsync<T>(Func<ValueTask<SessionPage<T>>> read)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage);
        try
        {
            RequireInspection();
            var page = await read().ConfigureAwait(false);
            RequireInspection();
            activity.Complete(HostOperationOutcome.Completed);
            return page;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private void RequireInspection()
    {
        if (!access.CanInspect)
        {
            throw new InvalidOperationException("Sessions inspection requires live private desktop ownership.");
        }
    }

    public async Task<WorkSessionAuthorization> ChangeLifecycleAsync(HostId<SessionIdentity> session,
        HostRevision expectedGeneration, bool active, RequestOrigin origin, CancellationToken token)
    {
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            throw new InvalidOperationException("Only explicit original trusted user input may control a session.");
        }
        var revision = access.ControlRevision;
        bool Eligible() => access.CanControl && access.ControlRevision == revision;
        var request = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        try
        {
            if (!Eligible())
            {
                throw new InvalidOperationException("Session control denied by privacy, call or ownership admission.");
            }
            var intent = await tasks.RecordIntentAsync(request, token).ConfigureAwait(false);
            WorkSessionAuthorization result;
            try
            {
                result = await store.ChangeIdleLifecycleAsync(request, expectedGeneration, active, Eligible, token).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                await tasks.RecordOutcomeAsync(intent, HostTaskState.Denied, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            // The authoritative lifecycle/audit transaction already committed. A receipt failure
            // must remain visible; it cannot be described as a rollback or replayed automatically.
            await tasks.RecordOutcomeAsync(intent, HostTaskState.Succeeded, CancellationToken.None).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    [LoggerMessage(182, LogLevel.Error, "Sessions workspace failed; exception type {ExceptionType}.")]
    private static partial void Failure(ILogger logger, string exceptionType);
}
