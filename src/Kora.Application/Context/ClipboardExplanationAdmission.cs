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

    public ClipboardExplanationAdmission(ISessionWorkspaceStore workspace, HostTaskCoordinator coordinator)
        : base(workspace, coordinator) => this.workspace = workspace;

    protected override async ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        (await workspace.CreateNamedSessionAsync(request, new("Local clipboard explanation"), eligible, token)
            .ConfigureAwait(false)).Authority;

    public Task<ClipboardExplanationResult> RunAsync(RequestOrigin origin, Func<bool> eligible,
        Func<HostRequest, Func<bool>, CancellationToken, Task<ClipboardExplanationResult>> operation, CancellationToken token) =>
        RunCommittedAsync(origin, eligible, (request, _, current, admitted) => operation(request, current, admitted),
            static result => result.Outcome == ClipboardExplanationOutcome.Succeeded
                ? HostTaskState.Succeeded : result.Outcome == ClipboardExplanationOutcome.Cancelled
                    ? HostTaskState.Cancelled : HostTaskState.Failed, token);
}