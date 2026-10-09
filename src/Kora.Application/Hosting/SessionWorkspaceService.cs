using System.Text;

using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Presentation;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService(
    ISessionWorkspaceStore store, HostTaskCoordinator tasks,
    ISessionWorkspaceAccess access, ILogger<SessionWorkspaceService> logger,
    SessionQueueService? queue = null)
{
    public event Action<HostTaskObservation>? WaitingTaskCancelled;
    public event Action<HostId<SessionIdentity>>? SessionRetired;
    public event Action<HostId<SessionIdentity>>? SessionLifecycleChanged;
    private SessionRetentionService? retention;
    public void BindRetention(SessionRetentionService service) => retention = service;
    private Kora.Application.Interaction.LocalEventBroker? localEvents;
    public void BindLocalEvents(Kora.Application.Interaction.LocalEventBroker broker) => localEvents = broker;

    private void NotifyCancellation(HostTaskObservation observation) => WaitingTaskCancelled?.Invoke(observation);
    public Task<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, CancellationToken token) =>
        ReadAsync(async () =>
        {
            var snapshot = queue is not null ? await queue.ReadWorkAsync(session, access.ControlRevision, token).ConfigureAwait(false)
                : await (store as ISessionWorkStore
                ?? throw new InvalidOperationException("The authoritative work snapshot service is unavailable."))
                .ReadWorkAsync(session, access.ControlRevision, new SessionQueueLimits(), token).ConfigureAwait(false);
            snapshot.RequireSubject(session);
            return snapshot;
        }, token);
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

    public Task<AdmittedDetailContent> ReadHistoryDetailAsync(HostId<SessionIdentity> session, Guid eventId, CancellationToken token) =>
        ReadAsync(async () =>
        {
            var record = await History.ReadHistoryEventAsync(session, eventId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The exact retained history receipt is unavailable. Refresh history.");
            token.ThrowIfCancellationRequested();
            if (record.SessionId != session || record.Id != eventId)
            {
                throw new InvalidDataException("History detail ownership does not match the exact requested receipt.");
            }
            return new AdmittedDetailContent(new(new(record.Id), record.Sequence), DetailContentKind.PlainText,
                DetailContentOrigin.SessionHistory, DetailSensitivity.DisclosureConfirmationRequired,
                "Host-committed history receipt",
                "Persisted session history; availability, baseline and provenance are recorded metadata, not an artifact body or execution authority.",
                Encoding.UTF8.GetString(SessionCommandResult.Serialize(new("observed", SessionHistoryPage.Scope)
                { HistoryEvent = record })), historySession: session);
        }, token);

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

    public async Task<WorkSessionAuthorization> ChangeLifecycleAsync(HostId<SessionIdentity> session,
        HostRevision expectedGeneration, bool active, RequestOrigin origin, CancellationToken token)
    {
        var result = await ControlAsync(session, origin,
            (request, eligible) => store.ChangeIdleLifecycleAsync(request, expectedGeneration, active, eligible, token), token).ConfigureAwait(false);
        SessionLifecycleChanged?.Invoke(session);
        return result;
    }

    internal Task<MemoryCommandResult> ExecuteMemoryControlAsync(HostId<SessionIdentity> session, RequestOrigin origin,
        Func<bool> admission, Func<HostRequest, Func<bool>, ValueTask<MemoryCommandResult>> operation, CancellationToken token)
    {
        // A callback cannot relabel its existing host/model context as a new local user action.
        if (HostActivity.Current is { } current) { origin = current.Request.Origin; }
        return ControlAsync(session, origin, operation, token, admission, existingSubject: true,
            terminalState: result => result.Outcome is "observed" or "Succeeded" ? HostTaskState.Succeeded : HostTaskState.Denied);
    }

    private async Task<T> ControlAsync<T>(HostId<SessionIdentity> session, RequestOrigin origin,
        Func<HostRequest, Func<bool>, ValueTask<T>> mutation, CancellationToken token,
        Func<bool>? additionalAdmission = null, bool inspection = false, bool existingSubject = false,
        bool terminalCommitted = false, Func<T, HostTaskState>? terminalState = null)
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
                await tasks.RecordOutcomeAsync(intent, terminalState?.Invoke(result) ?? HostTaskState.Succeeded, CancellationToken.None).ConfigureAwait(false);
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
