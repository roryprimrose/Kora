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

    public Task<SessionPage<SessionWorkspaceEntry>> ReadMetadataAsync(Guid? after, int limit, CancellationToken token) =>
        ReadAsync(() => store.ReadMetadataPageAsync(after, limit, token));

    public Task<SessionWorkspaceEntry> CreateAsync(SessionName name, RequestOrigin origin, CancellationToken token) =>
        ControlAsync(new(Guid.NewGuid()), origin,
            (request, eligible) => store.CreateNamedSessionAsync(request, name, eligible, token), token);

    public Task<SessionWorkspaceEntry> RenameAsync(HostId<SessionIdentity> session, HostRevision expectedGeneration,
        long expectedMetadataRevision, SessionName name, RequestOrigin origin, CancellationToken token) =>
        ControlAsync(session, origin,
            (request, eligible) => store.RenameSessionAsync(request, expectedGeneration, expectedMetadataRevision, name, eligible, token), token);

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

    public Task<WorkSessionAuthorization> ChangeLifecycleAsync(HostId<SessionIdentity> session,
        HostRevision expectedGeneration, bool active, RequestOrigin origin, CancellationToken token) =>
        ControlAsync(session, origin,
            (request, eligible) => store.ChangeIdleLifecycleAsync(request, expectedGeneration, active, eligible, token), token);

    private async Task<T> ControlAsync<T>(HostId<SessionIdentity> session, RequestOrigin origin,
        Func<HostRequest, Func<bool>, ValueTask<T>> mutation, CancellationToken token,
        Func<bool>? additionalAdmission = null, bool inspection = false)
    {
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            throw new InvalidOperationException("Only explicit original trusted user input may control a session.");
        }
        var revision = access.ControlRevision;
        bool Eligible() => (inspection ? access.CanInspect : access.CanControl)
            && access.ControlRevision == revision && (additionalAdmission?.Invoke() ?? true);
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
}
