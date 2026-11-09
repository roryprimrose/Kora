using System.Diagnostics;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Core.Tools;

namespace Kora.Application.Maintenance;

public sealed partial class MaintenanceCommands
{
    private async Task<WorkSessionAuthorization> EnsureControlSessionAsync(RequestOrigin origin, Func<bool> admitted, CancellationToken token)
    {
        if (session is { } existing) { return existing; }
        var creation = HostRequest.Create(origin);
        using var activity = HostActivity.BeginRoot(creation, HostActivityLayer.Application, HostOperation.Policy);
        var intent = await workspace.RecordControlIntentAsync(creation, token).ConfigureAwait(false);
        WorkSessionAuthorization created;
        try
        {
            created = await sessions.CreateMaintenanceControlSessionAsync(creation, admitted, token).ConfigureAwait(false);
            session = created;
            await tasks.RecordOutcomeAsync(intent, HostTaskState.Succeeded, CancellationToken.None).ConfigureAwait(false);
        }
        catch { unavailable = true; throw; }
        activity.Complete(HostOperationOutcome.Completed);
        return created;
    }

    public async ValueTask<CapabilityReply> InvokeAsync(string id, string input, Func<bool> admitted, CancellationToken token)
    {
        var caller = HostActivity.Current;
        if (caller is null || caller.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { return new(CapabilityOutcome.Denied, "original-maintenance-request-required"); }
        var descriptor = MaintenanceTools.FindExact(id);
        if (descriptor is null) { return new(CapabilityOutcome.Denied, "unknown-maintenance-tool"); }
        var parsed = CapabilityInputPolicy.Read(descriptor, input);
        if (parsed.DenialReason is { } denial) { return new(CapabilityOutcome.Denied, denial); }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var cancellation = linked.Token;
        bool Current() => !disposed && !unavailable && admitted()
            && caller.Outcome == HostOperationOutcome.Unknown && caller.Activity?.IsStopped == false;
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        Kora.Core.Authorization.WorkSessionAuthorization authority;
        try
        {
            if (!Current()) { return new(CapabilityOutcome.Denied, "maintenance-host-unavailable"); }
            authority = await EnsureControlSessionAsync(caller.Request.Origin, Current, cancellation).ConfigureAwait(false);
        }
        finally { gate.Release(); }

        var request = new HostRequest(new(Guid.NewGuid()), authority.SessionId, new(Guid.NewGuid()), caller.Request.Origin);
        using var operation = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request,
            [new ActivityLink(caller.Activity!.Context)]);
        var intent = await workspace.RecordControlIntentAsync(request, cancellation).ConfigureAwait(false);
        var terminal = false;
        try
        {
            await RevalidateAsync();
            await state.ExecuteUpdateAsync(parsed.ReleaseVersion, Current, cancellation).ConfigureAwait(false);
            await RevalidateAsync();
            CapabilityReply reply;
            if (!state.HasVerifiedMcpObservation || parsed.ReleaseVersion is { } exact
                && !string.Equals(state.ReadMcpObservation().ReleaseVersion, exact.ToString(), StringComparison.Ordinal))
            { reply = new(CapabilityOutcome.Denied, "maintenance-verification-unavailable"); }
            else if (string.Equals(id, MaintenanceTools.Check, StringComparison.Ordinal))
            { reply = new(CapabilityOutcome.Succeeded, MaintenanceTools.ResultReason, Maintenance: state.ReadMcpObservation()); }
            else
            {
                var started = await state.RequestMcpInstallerAsync(parsed.ReleaseVersion, Current, cancellation).ConfigureAwait(false);
                reply = started == InstallerStartOutcome.Started
                    ? new(CapabilityOutcome.Succeeded, MaintenanceTools.ResultReason, Maintenance: state.ReadMcpObservation(started))
                    : new(CapabilityOutcome.Denied, started == InstallerStartOutcome.Unknown
                        ? "installer-start-outcome-unconfirmed" : "installer-not-started");
            }
            await RevalidateAsync();
            try { await tasks.RecordOutcomeAsync(intent, reply.Outcome == CapabilityOutcome.Succeeded
                ? HostTaskState.Succeeded : HostTaskState.Failed, CancellationToken.None).ConfigureAwait(false); }
            catch { unavailable = true; throw; }
            terminal = true;
            cancellation.ThrowIfCancellationRequested();
            if (!Current()) { throw new InvalidOperationException("Maintenance MCP result returned after its caller retired."); }
            operation.Complete(reply.Outcome == CapabilityOutcome.Succeeded ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return reply;
        }
        catch (Exception exception)
        {
            operation.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            if (!terminal) { await tasks.RecordOutcomeAsync(intent, HostTaskState.Failed, CancellationToken.None).ConfigureAwait(false); }
            throw;
        }

        async Task RevalidateAsync()
        {
            cancellation.ThrowIfCancellationRequested();
            await sessions.WithMaintenanceControlSessionAsync(request, authority.Generation,
                () => Current() ? true : throw new InvalidOperationException("Maintenance MCP admission changed."),
                cancellation).ConfigureAwait(false);
        }
    }
}
