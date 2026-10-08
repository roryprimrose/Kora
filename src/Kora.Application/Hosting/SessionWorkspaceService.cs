using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService(
    ISessionWorkspaceStore store, HostTaskCoordinator tasks,
    ISessionWorkspaceAccess access, ILogger<SessionWorkspaceService> logger,
    SessionQueueService? queue = null)
{
    public event Action<HostTaskObservation>? WaitingTaskCancelled;
    private SessionRetentionService? retention;
    public void BindRetention(SessionRetentionService service) => retention = service;

    private void NotifyCancellation(HostTaskObservation observation) => WaitingTaskCancelled?.Invoke(observation);
    public Task<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, CancellationToken token) =>
        ReadAsync(async () =>
        {
            var snapshot = await (store as ISessionWorkStore
                ?? throw new InvalidOperationException("The authoritative work snapshot service is unavailable."))
                .ReadWorkAsync(session, access.ControlRevision, queue?.Limits ?? new SessionQueueLimits(), token).ConfigureAwait(false);
            snapshot.RequireSubject(session);
            return snapshot;
        });
    public Task<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) =>
        ReadAsync(() => store.ReadSessionsAsync(after, limit, token), token);

    public Task<SessionPage<SessionWorkspaceEntry>> ReadMetadataAsync(Guid? after, int limit, CancellationToken token) =>
        ReadAsync(() => store.ReadMetadataPageAsync(after, limit, token), token);

    public Task<SessionWorkspaceEntry> CreateAsync(SessionName name, RequestOrigin origin, CancellationToken token) =>
        ControlAsync(new(Guid.NewGuid()), origin,
            (request, eligible) => store.CreateNamedSessionAsync(request, name, eligible, token), token);

    public Task<SessionWorkspaceEntry> RenameAsync(HostId<SessionIdentity> session, HostRevision expectedGeneration,
        long expectedMetadataRevision, SessionName name, RequestOrigin origin, CancellationToken token) =>
        ControlAsync(session, origin,
            (request, eligible) => store.RenameSessionAsync(request, expectedGeneration, expectedMetadataRevision, name, eligible, token), token);

    public Task<SessionPage<HostQuestionRecord>> ReadQuestionsAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) =>
        ReadAsync(() => store.ReadQuestionPageAsync(session, after, limit, token), token);

    public Task<SessionPage<HostTaskRecord>> ReadTasksAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) =>
        ReadAsync(() => store.ReadTaskPageAsync(session, after, limit, token), token);

    public Task<SessionHistoryPage> ReadHistoryAsync(HostId<SessionIdentity> session, SessionHistoryCursor? cursor,
        int limit, CancellationToken token) =>
        ReadAsync(() => History.ReadHistoryAsync(session, cursor, limit, token), token);

    public Task<SessionHistoryEvent?> ReadHistoryEventAsync(HostId<SessionIdentity> session, Guid eventId, CancellationToken token) =>
        ReadAsync(() => History.ReadHistoryEventAsync(session, eventId, token), token);

    private ISessionHistoryStore History => store as ISessionHistoryStore
        ?? throw new InvalidOperationException("The admitted store does not provide durable history.");

    public async Task<HostTaskObservation> CancelTaskAsync(HostTaskCancellationTarget target, RequestOrigin origin,
        Func<bool> admission, CancellationToken token)
    {
        var result = await ControlAsync(target.SessionId, origin,
            (request, eligible) => store.CancelWaitingTaskAsync(request, target, eligible, token),
            token, admission, inspection: true, existingSubject: true).ConfigureAwait(false);
        NotifyCancellation(result);
        return result;
    }

    private async Task<T> ReadAsync<T>(Func<ValueTask<T>> read, CancellationToken token)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage);
        try
        {
            RequireInspection();
            if (retention is not null) { await retention.RunAsync(token).ConfigureAwait(false); }
            var revision = access.ControlRevision;
            var page = await read().ConfigureAwait(false);
            RequireInspection();
            if (access.ControlRevision != revision)
            {
                throw new InvalidOperationException("Session inspection requires unchanged private admission during retrieval.");
            }
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

    public Task<WorkSessionAuthorization> ChangeLifecycleAsync(HostId<SessionIdentity> session,
        HostRevision expectedGeneration, bool active, RequestOrigin origin, CancellationToken token) =>
        ControlAsync(session, origin,
            (request, eligible) => store.ChangeIdleLifecycleAsync(request, expectedGeneration, active, eligible, token), token);

    private async Task<T> ControlAsync<T>(HostId<SessionIdentity> session, RequestOrigin origin,
        Func<HostRequest, Func<bool>, ValueTask<T>> mutation, CancellationToken token,
        Func<bool>? additionalAdmission = null, bool inspection = false, bool existingSubject = false,
        bool terminalCommitted = false)
    {
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            throw new InvalidOperationException("Only explicit original trusted user input may control a session.");
        }
        var revision = access.ControlRevision;
        bool Eligible() => (inspection ? access.CanInspect : access.CanControl)
            && access.ControlRevision == revision && (additionalAdmission?.Invoke() ?? true);
        if (existingSubject)
        {
            if (!Eligible()) { throw new InvalidOperationException("Task control requires current private host admission."); }
            var resolved = await store.ReadMetadataAsync(session, token).ConfigureAwait(false);
            if (resolved.Authority.SessionId != session)
            {
                throw new InvalidDataException("The exact existing task session could not be resolved.");
            }
            token.ThrowIfCancellationRequested();
            if (!Eligible()) { throw new InvalidOperationException("Task session admission changed during resolution."); }
        }
        var request = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        try
        {
            if (!Eligible())
            {
                throw new InvalidOperationException("Session control denied by privacy, call or ownership admission.");
            }
            var intent = await store.RecordControlIntentAsync(request, token).ConfigureAwait(false);
            T result;
            try
            {
                result = await mutation(request, Eligible).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                await tasks.RecordOutcomeAsync(intent, HostTaskState.Denied, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            // The authoritative lifecycle/audit transaction already committed. A receipt failure
            // must remain visible; it cannot be described as a rollback or replayed automatically.
            if (!terminalCommitted)
            {
                await tasks.RecordOutcomeAsync(intent, HostTaskState.Succeeded, CancellationToken.None).ConfigureAwait(false);
            }
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
}
