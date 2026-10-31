using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;

namespace Kora.Application.Skills;

public sealed class OwnedSkillControlAdmission(
    ISessionWorkspaceStore workspace, IOwnedSkillControlSessionStore sessions, HostTaskCoordinator coordinator)
    : HostControlAdmission(workspace, coordinator)
{
    internal ValueTask<HostTaskObservation?> ReadIntent(HostRequest request, CancellationToken token) =>
        Workspace.ReadTaskAsync(request.SessionId, request.TaskId, token);

    protected override ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        sessions.CreateOwnedSkillSession(request, eligible, token);

    public async Task<T> Run<T>(OwnedSkillAuthoringAuthority authority, Func<bool> eligible,
        Func<HostRequest, WorkSessionAuthorization, Func<bool>, CancellationToken, Task<T>> operation, CancellationToken token)
    {
        var parent = Activity.Current;
        var links = new List<ActivityLink>();
        if (authority.OriginalActivityContext is { } original) { links.Add(new(original)); }
        if (parent is not null && parent.Context != authority.OriginalActivityContext) { links.Add(new(parent.Context)); }
        using var activity = HostActivity.BeginRoot(HostRequest.Create(authority.Origin), HostActivityLayer.Application,
            HostOperation.Request, links);
        try
        {
            var result = await RunCommittedAsync(authority.Origin, eligible, async (request, session, current, admitted) =>
            {
                try { return new Completion<T>(await operation(request, session, current, admitted).ConfigureAwait(false), null, HostTaskState.Succeeded); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    return new(default!, ExceptionDispatchInfo.Capture(exception),
                        exception is OwnedSkillPublicationUnconfirmedException ? HostTaskState.Unknown
                        : exception is OperationCanceledException ? HostTaskState.Cancelled : HostTaskState.Failed);
                }
            }, static outcome => outcome.State, token).ConfigureAwait(false);
            result.Failure?.Throw();
            activity.Complete(HostOperationOutcome.Completed);
            return result.Value;
        }

        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    private sealed record Completion<T>(T Value, ExceptionDispatchInfo? Failure, HostTaskState State);

    public ValueTask<T> Commit<T>(HostRequest request, WorkSessionAuthorization session,
        Func<bool> eligible, Func<T> operation, CancellationToken token) =>
        sessions.WithOwnedSkillSession(request, session.Generation, () =>
        {
            token.ThrowIfCancellationRequested();
            if (!eligible() || !OwnsLiveContext(request))
            { throw new InvalidOperationException("Original skill authoring authority is no longer current."); }
            return operation();
        }, token);
}
