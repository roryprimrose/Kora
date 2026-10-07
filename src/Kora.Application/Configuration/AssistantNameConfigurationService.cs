using Kora.Application.Communication;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class AssistantNameConfigurationService(
    IAssistantNamePreferences preferences, BuiltInCommandCatalog commands,
    ISecurityAuditLog auditLog, ILogger<AssistantNameConfigurationService> logger) : IDisposable
{
    private readonly Lock gate = new();
    private readonly SemaphoreSlim mutations = new(1, 1);
    private readonly Guid owner = Guid.NewGuid();
    private AssistantNameConfigurationState? state;
    private bool publishing;

    public event EventHandler? Changed;

    public AssistantNameConfigurationState Get()
    {
        lock (gate)
        {
            if (state is null) { Reload(); }
            return state!;
        }
    }

    public void Reload()
    {
        lock (gate)
        {
            EnsureNotPublishing();
            AssistantNameConfigurationState next;
            try
            {
                var saved = preferences.LoadName();
                var name = Normalize(saved ?? AssistantNameRules.DefaultName);
                next = new(name, state?.Revision ?? 0, saved is not null);
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException
                or IOException or UnauthorizedAccessException)
            {
                ReadFailed(logger, exception);
                next = new(null, state?.Revision ?? 0, true,
                    "The saved assistant name is invalid or unreadable (including conflicts with a built-in command). Repair preference access, then explicitly set/reset assistant.name or refresh. Prefix routing and capture are unavailable; native recovery remains available.");
            }
            if (state is null || state != next)
            {
                state = next with { Revision = checked(next.Revision + 1) };
                Publish();
            }
        }
    }

    public AssistantNameProposal Propose(string value, long revision, long callRevision,
        RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        if (!Enum.IsDefined(initiator))
        {
            throw new ArgumentOutOfRangeException(nameof(initiator));
        }
        var request = HostActivity.Current?.Request ?? HostRequest.Create(origin);
        // Explicit voice provenance cannot be laundered by a later UI caller.
        if (initiator == SecurityAuditInitiator.VoiceCommand && request.Origin != RequestOrigin.ActivatedVoice)
        {
            request = new(request.RequestId, request.SessionId, request.TaskId,
                RequestOrigin.ActivatedVoice, request.InvocationId);
        }
        return new(owner, value, revision, callRevision, request, initiator);
    }

    public AssistantNameProposal ProposeReset(long revision, long callRevision,
        RequestOrigin origin, SecurityAuditInitiator initiator) =>
        Propose(AssistantNameRules.DefaultName, revision, callRevision, origin, initiator);

    internal async Task<AssistantNameApplyResult> ApplyAsync(
        AssistantNameProposal proposal, CallCommunicationPolicy policy, Func<bool> hostEligible,
        Func<Task> retireCapture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        lock (gate)
        {
            EnsureNotPublishing();
            if (proposal.Owner != owner)
            {
                throw new ArgumentException("The assistant name proposal belongs to another host registry.", nameof(proposal));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            AssistantNameOption.AuditAction, SecurityAuditOutcome.Requested,
            proposal.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : proposal.Initiator,
            "preferences.device-local");
        var original = HostActivity.Current?.Request;
        using var activity = HostActivity.BeginAudit(proposal.Request, audit);
        auditLog.Write(audit);
        var acquired = false;
        var committed = false;
        var published = false;
        var stage = "capture-stop-failed";
        try
        {
            await mutations.WaitAsync(cancellationToken);
            acquired = true;
            AssistantNameApplyResult Reject(string reason, string error)
            {
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason));
                activity.Complete(HostOperationOutcome.Failed);
                Rejected(logger, reason);
                return new(false, false, Get(), error, reason);
            }
            if (original is not null && (original.RequestId != proposal.Request.RequestId
                || original.SessionId != proposal.Request.SessionId || original.TaskId != proposal.Request.TaskId))
            {
                return Reject("host-context-changed", "The original host context changed. Submit a new request.");
            }
            if (proposal.Initiator is not (SecurityAuditInitiator.LocalUser
                or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand))
            {
                return Reject("origin-denied", "Only a local user, typed command or activated voice request may change this setting.");
            }
            string name;
            try { name = Normalize(proposal.Value); }
            catch (ArgumentException exception) { return Reject("invalid-name", exception.Message); }
            var current = Get();
            if (current.Revision != proposal.Revision)
            {
                return Reject("stale", "Assistant configuration changed. Inspect it and submit a new request.");
            }
            var denied = policy.CheckMutation(proposal.Origin, proposal.CallRevision, hostEligible);
            if (denied is not null)
            {
                return Reject(denied.Value.ToString().ToLowerInvariant(), "Call policy changed or the host/original channel is unavailable. Start a new eligible Settings request.");
            }
            if (current.IsAvailable && string.Equals(name, current.Name, StringComparison.Ordinal))
            {
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Cancelled, "no-change"));
                activity.Complete(HostOperationOutcome.Completed);
                return new(true, false, current);
            }
            await retireCapture();
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                EnsureNotPublishing();
                if (Get().Revision != proposal.Revision)
                {
                    return Reject("stale", "Assistant configuration changed while capture closed. Submit a new request.");
                }
                stage = "persistence-failed";
                denied = policy.CommitVoiceSetting(proposal.Origin, proposal.CallRevision, hostEligible, () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    preferences.SaveName(name);
                    committed = true;
                });
                if (denied is not null)
                {
                    return Reject(denied.Value.ToString().ToLowerInvariant(), "Call policy changed or host admission expired while capture closed. Nothing is queued for later.");
                }
                stage = "audit-failed";
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                activity.Complete(HostOperationOutcome.Completed);
                state = new(name, checked(current.Revision + 1), true);
                Publish();
                published = true;
                return new(true, true, state);
            }
        }
        catch (OperationCanceledException) when (!committed)
        {
            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Cancelled, "user-cancelled"));
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            activity.Complete(HostOperationOutcome.Failed);
            MutationFailed(logger, stage, exception);
            if (committed)
            {
                throw;
            }
            var reason = string.Equals(stage, "capture-stop-failed", StringComparison.Ordinal) ? stage
                : exception is UnauthorizedAccessException ? "access-denied" : "io-error";
            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, reason));
            return new(false, false, Get(), exception.Message, reason);
        }
        finally
        {
            try
            {
                if (committed && !published)
                {
                    lock (gate)
                    {
                        CompletionUnconfirmed(logger);
                        state = new(null, checked(Get().Revision + 1), true,
                            "The preference was replaced but its audit/application completion was not confirmed. Capture remains held. Inspect evidence and explicitly refresh or set/reset the name; no success is claimed.");
                        Publish();
                    }
                }
            }
            finally
            {
                if (acquired) { mutations.Release(); }
            }
        }
    }

    private string Normalize(string value)
    {
        var name = AssistantNameRules.Normalize(value);
        _ = commands.GetCommands(name);
        return name;
    }

    private void EnsureNotPublishing()
    {
        if (publishing) { throw new InvalidOperationException("Assistant configuration cannot mutate during change notification."); }
    }

    private void Publish()
    {
        publishing = true;
        try { Changed?.Invoke(this, EventArgs.Empty); }
        finally { publishing = false; }
    }

    public void Dispose() => mutations.Dispose();
}
