using System.Diagnostics;

using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Original-channel, single-use review of one exact conversation's action policy.</summary>
public sealed class SessionPermissionCommandService(
    SessionWorkspaceService workspace, ISessionWorkspaceAccess access, TimeProvider time)
{
    private readonly Lock gate = new();
    private Pending? pending;
    private long epoch;

    public void Retire()
    {
        lock (gate) { pending = null; epoch++; }
    }

    public async Task<SessionCommandResult> ExecuteAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token, Func<bool>? reviewSourceAdmitted = null)
    {
        Pending? previous;
        long attempt;
        lock (gate) { previous = pending; pending = null; attempt = ++epoch; }
        SessionConversationService.RequireOriginalSource(origin);
        var session = new HostId<SessionIdentity>(command.SessionId
            ?? throw new InvalidOperationException("An exact existing session ID is required."));
        var control = access.ControlRevision;
        bool Live() => !token.IsCancellationRequested && access.CanInspect && access.CanControl
            && access.ControlRevision == control && admitted();
        bool ReviewSourceLive() => !token.IsCancellationRequested && access.CanInspect && access.CanControl
            && access.ControlRevision == control && (reviewSourceAdmitted ?? admitted)();
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Policy, previous is null ? [] : [previous.Cause]);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Live()) { throw new InvalidOperationException("Original private permission input changed. Start a fresh review."); }
            if (command.Operation is SessionCommandOperation.PermissionStatus or SessionCommandOperation.PermissionReview)
            {
                var target = await workspace.ReadExactMetadataAsync(session, token).ConfigureAwait(false);
                var policy = await workspace.ReadSessionPermissionAsync(session, token).ConfigureAwait(false);
                policy.Validate(target.Authority);
                if (!Live()) { throw new InvalidOperationException("Permission observation admission changed."); }
                var label = $"Conversation {target.Metadata?.Name.Value ?? "Unnamed"} ({session.Value:D}): {policy.DisplayName}. ";
                if (command.Operation == SessionCommandOperation.PermissionStatus)
                {
                    activity.Complete(HostOperationOutcome.Completed);
                    return new("observed", label + "Passive policy observation; no permission, activity, grant or execution changed.");
                }
                if (!target.Authority.IsActive || command.PermissionLevel is not { } desired || !Enum.IsDefined(desired))
                { throw new InvalidOperationException("Review requires an Active conversation and an exact supported permission level."); }
                var next = (policy with { Level = desired }).DisplayName;
                lock (gate)
                {
                    if (!Live() || !ReviewSourceLive() || epoch != attempt)
                    { throw new InvalidOperationException("Permission review was retired during observation."); }
                    pending = new(target, policy, desired, origin, time.GetUtcNow().AddSeconds(60), ReviewSourceLive, new(activity.Activity!.Context));
                }
                activity.Complete(HostOperationOutcome.Completed);
                return new("review", label + $"Review change to {next}. "
                    + (desired == SessionPermissionLevel.ApproveAll ? SessionPermissionPolicy.ApproveAllWarning
                        : desired == SessionPermissionLevel.ModelReview
                            ? "An unavailable or uncertain independent tools-disabled reviewer requires your approval."
                            : "Otherwise-ungranted actions require your approval.")
                    + $" Original channel: {origin}. Confirm separately within 60 seconds: session permission confirm {session.Value:D}"
                    + $". Or discard: session permission cancel {session.Value:D}. Nothing changed; no grants or actions created.");
            }
            if (previous is null || previous.Policy.SessionId != session || previous.Origin != origin)
            { throw new InvalidOperationException("No current permission review matches this exact conversation and original channel. Review again."); }
            if (command.Operation == SessionCommandOperation.PermissionCancel)
            {
                activity.Complete(HostOperationOutcome.Completed);
                return new("cancelled", "Permission review discarded. Policy, grants and work are unchanged.");
            }
            if (command.Operation != SessionCommandOperation.PermissionConfirm)
            { throw new InvalidOperationException("Only permission status, review, confirm or cancel is supported."); }
            bool OriginalLive()
            {
                lock (gate)
                {
                    return Live() && previous.SourceAdmitted() && time.GetUtcNow() < previous.ExpiresAt && epoch == attempt;
                }
            }
            if (!OriginalLive()) { throw new InvalidOperationException("Original permission source or review expired. Review again."); }
            var actual = await workspace.ReadExactMetadataAsync(session, token).ConfigureAwait(false);
            if (actual != previous.Target || !OriginalLive())
            { throw new InvalidOperationException("The reviewed conversation or private source changed. Review again."); }
            var committed = await workspace.SetReviewedSessionPermissionAsync(previous.Policy, previous.Desired, origin,
                OriginalLive, token).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Completed);
            return new("committed", $"Exact conversation {session.Value:D}: {committed.DisplayName}. "
                + "Policy and required audit committed. Old pending approvals and undispatched admissions retired; "
                + "no grant, execution, replay or provider enablement created.");
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    private sealed record Pending(SessionWorkspaceEntry Target, SessionPermissionPolicy Policy, SessionPermissionLevel Desired,
        RequestOrigin Origin, DateTimeOffset ExpiresAt, Func<bool> SourceAdmitted, ActivityLink Cause);
}
