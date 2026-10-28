using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Application.Context;

/// <summary>Consumes real tool grants, never classifies clipboard content.</summary>
public sealed class ClipboardPermissionSourceUsePolicy(
    IHostInteractionStore store, ISessionWorkspaceStore workspace, TimeProvider time) : IClipboardSourceUsePolicy
{
    public const string ActionId = "clipboard.explain-local";
    private ClipboardPermissionPresenter? present;
    private ClipboardSnapshot? source;
    private HostRequest? invocation;
    private SessionPermissionAdmission? permission;

    public void BindPresenter(ClipboardPermissionPresenter presenter) => present = presenter;

    public async Task<ClipboardSourceUseOutcome> EvaluateAsync(
        ClipboardSnapshot snapshot, Func<bool> current, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(current);
        token.ThrowIfCancellationRequested();
        source = null;
        permission = null;
        invocation = null;
        if (present is not { } presenter) { return ClipboardSourceUseOutcome.Unavailable; }
        var host = HostActivity.RequireCurrent();
        var request = host.Request;
        var expiresAt = snapshot.CapturedAt + ClipboardExplanationContext.Lifetime;
        if (!ClipboardCommand.IsDeliberateOrigin(request.Origin) || !current()
            || time.GetUtcNow() < snapshot.CapturedAt || time.GetUtcNow() >= expiresAt)
        { return ClipboardSourceUseOutcome.Denied; }
        var session = await workspace.ReadMetadataAsync(request.SessionId, token).ConfigureAwait(false);
        if (!current() || !session.Authority.IsActive || session.Authority.SessionId != request.SessionId)
        { return ClipboardSourceUseOutcome.Denied; }
        var use = new HostRequest(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid()));
        using var activity = HostActivity.BeginRoot(use, HostActivityLayer.Application, HostOperation.Policy,
            [new ActivityLink(host.Activity!.Context)]);
        var proposal = new HostOperationProposal(use, new(Guid.NewGuid()), new(1), CreateBinding(),
            HostOperationEffect.BoundedRead, expiresAt);
        var decision = await presenter(proposal, session.Authority.Generation, current, token).ConfigureAwait(false);
        if (!current() || decision is not { Outcome: HostInteractionOutcome.Consumed, Grant: { } grant, Admission: { } admitted }
            || admitted.Proposal != proposal || admitted.Basis != SessionPermissionBasis.ExistingGrant
            || admitted.GrantId != grant.Id || admitted.GrantRevision != grant.Revision
            || grant.ApprovedProposal.Binding != proposal.Binding
            || grant.CreatorChannel != RequestOrigin.LocalUi)
        {
            activity.Complete(HostOperationOutcome.Failed);
            return ClipboardSourceUseOutcome.Denied;
        }
        var dispatched = await new HostAuthorizationService(store, time).DispatchAsync(use, admitted, token).ConfigureAwait(false);
        if (!current() || dispatched is not { Outcome: HostInteractionOutcome.Consumed, Admission: { } receipt })
        {
            activity.Complete(HostOperationOutcome.Failed);
            return ClipboardSourceUseOutcome.Denied;
        }
        source = snapshot;
        invocation = use;
        permission = receipt;
        activity.Complete(HostOperationOutcome.Completed);
        return ClipboardSourceUseOutcome.Accepted;
    }

    public async Task ValidateAsync(ClipboardSnapshot snapshot, CancellationToken token)
    {
        var host = HostActivity.RequireCurrent();
        if (!ReferenceEquals(snapshot, source) || invocation is not { } use || permission is not { } receipt
            || !use.IsWithinIntent(host.Request))
        { throw new InvalidOperationException("The exact clipboard permission is unavailable."); }
        using var activity = HostActivity.BeginRoot(use, HostActivityLayer.Application, HostOperation.Policy,
            [new ActivityLink(host.Activity!.Context)]);
        var decision = await new HostAuthorizationService(store, time)
            .ValidateAdmissionAsync(use, receipt, token).ConfigureAwait(false);
        if (decision.Outcome != HostInteractionOutcome.Approved)
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw new InvalidOperationException("The exact clipboard permission was revoked, expired or retired.");
        }
        activity.Complete(HostOperationOutcome.Completed);
    }

    public static ExactOperationBinding CreateBinding()
    {
        // A reusable grant covers this built-in operation, not a content digest or an unreviewed model turn.
        var definition = Hash("builtin.clipboard.explain-local.v1");
        return new(ActionId, "builtin", "clipboard", definition, definition, definition,
            Hash(typeof(ClipboardPermissionSourceUsePolicy).Module.ModuleVersionId.ToString("D")),
            Hash("explicit-reviewed-snapshot"), Hash("clipboard.snapshot"), Hash("original-local-user"),
            Hash("local-only"), Hash("volatile-no-tools-no-speech"), new(1));
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
