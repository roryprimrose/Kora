using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Context;

/// <summary>Content-free clipboard task authority, separate from retained conversations.</summary>
public sealed class ClipboardExplanationAdmission : HostControlAdmission
{
    private readonly ISessionWorkspaceStore workspace;
    private readonly ILocalContextTaskStore contexts;

    public ClipboardExplanationAdmission(ISessionWorkspaceStore workspace, ILocalContextTaskStore contexts,
        HostTaskCoordinator coordinator) : base(workspace, coordinator)
    {
        this.workspace = workspace;
        this.contexts = contexts;
    }

    protected override async ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        (await workspace.CreateNamedSessionAsync(request, new("Local clipboard explanation"), eligible, token)
            .ConfigureAwait(false)).Authority;

    protected override ValueTask<HostTaskRecord> AdmitOperationAsync(HostTaskRecord intent,
        WorkSessionAuthorization session, Func<bool> eligible, CancellationToken token) =>
        contexts.AdmitLocalContextDispatchAsync(intent.Request, session.Generation, eligible, token);

    public Task<ClipboardExplanationResult> RunAsync(RequestOrigin origin, Func<bool> eligible,
        Func<HostRequest, Func<bool>, CancellationToken, Task<ClipboardExplanationResult>> operation, CancellationToken token) =>
        RunCommittedAsync(origin, eligible, (request, _, current, admitted) => operation(request, current, admitted),
            static result => result.Outcome == ClipboardExplanationOutcome.Succeeded
                ? HostTaskState.Succeeded : result.Outcome == ClipboardExplanationOutcome.Cancelled
                    ? HostTaskState.Unknown : HostTaskState.Failed, token);
}