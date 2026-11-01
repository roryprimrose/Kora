using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

/// <summary>One reviewed, original-user preference operation shared by native, typed and activated input.</summary>
public sealed class ConversationExecutionConfigurationService(
    IConversationExecutionPreferences preferences, AudioControlAdmission admission, ISecurityAuditLog audit)
{
    private readonly Lock gate = new();
    private ConversationExecutionLimit? saved;
    private volatile ConversationExecutionBudget? budget;
    private ConversationExecutionReview? review;
    private long revision;
    private bool applying;

    public event EventHandler? Changed;
    internal ConversationExecutionBudget? Current => budget;
    public ConversationExecutionReview? Review { get { lock (gate) { return review; } } }
    public string Description => budget is { } current
        ? $"Configured future ordinary-conversation maximum: {current.Limit.Value} ({(current.Saved ? "saved" : "default")}). "
            + "This is not qualified effective capacity. Each exact provider/profile must independently admit its resources; "
            + "unknown readiness is unavailable and unknown resource costs remain exclusive. "
            + "A lower maximum never cancels executing work. Pending per-session FIFO is unchanged."
        : "Conversation execution preference unavailable. New resource admission is held; inspect saved state and audit, then refresh.";

    public void Observe()
    {
        lock (gate)
        {
            if (applying) { throw new InvalidOperationException("Conversation execution preference is committing."); }
            try
            {
                var value = preferences.Load();
                if (budget is not null && saved == value) { return; }
                saved = value;
                review = null;
                budget = new(value ?? ConversationExecutionLimit.Default, checked(++revision), value is not null);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch { HoldUnavailable(); throw; }
        }
    }

    internal ConversationExecutionBudget? Capture()
    {
        lock (gate)
        {
            if (budget is null) { return null; }
            try
            {
                if (preferences.Load() != saved)
                { throw new InvalidDataException("Saved conversation execution maximum changed. Explicitly refresh before new admission."); }
                return budget;
            }
            catch { HoldUnavailable(); throw; }
        }
    }

    public async Task<ConversationExecutionReview> ReviewAsync(ConversationExecutionLimit proposed, long callRevision,
        RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        var candidate = await admission.RunAsync(origin, eligible, (_, authority) =>
        {
            Observe();
            lock (gate)
            {
                return review = new(proposed, budget!, callRevision, authority, origin, eligible);
            }
        }, token).ConfigureAwait(false);
        if (!eligible()) { CancelReview(); throw new InvalidOperationException("Original preference review admission changed."); }
        return candidate;
    }

    public Task RefreshAsync(RequestOrigin origin, Func<bool> eligible, CancellationToken token) =>
        admission.RunAsync(origin, eligible, (_, _) =>
        {
            Observe();
            return true;
        }, token);

    public void CancelReview()
    {
        lock (gate) { review = null; }
    }

    public async Task<bool> ConfirmAsync(ConversationExecutionReview candidate, RequestOrigin origin,
        SecurityAuditInitiator initiator, CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        { throw new InvalidOperationException("Conversation execution settings require original admitted user input."); }
        var entered = false;
        try
        {
            var accepted = await admission.RunAsync(origin, eligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (applying || !ReferenceEquals(review, candidate) || !ReferenceEquals(budget, candidate.Budget)
                        || candidate.Session != authority || candidate.Origin != request.Origin
                        || !candidate.Eligible() || preferences.Load() != saved)
                    { throw new InvalidOperationException("Conversation execution review, saved preference or original authority changed. Review again."); }
                    applying = true;
                    entered = true;
                    review = null;
                    var entry = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                        "configuration.conversation-execution-maximum", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                        "preferences.device-local");
                    audit.Write(entry);
                    var denied = policy.CommitVoiceSetting(request.Origin, candidate.CallRevision,
                        () => eligible() && candidate.Eligible() && !token.IsCancellationRequested, () =>
                        {
                            budget = null;
                            try
                            {
                                if (preferences.Load() != saved)
                                { throw new InvalidDataException("Saved conversation execution maximum changed during required audit."); }
                                preferences.BeginWrite();
                                preferences.Save(candidate.Proposed);
                                RequireReadback();
                            }
                            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                            {
                                audit.Write(entry.WithOutcome(SecurityAuditOutcome.Failed, "preference-write-unconfirmed"));
                                throw;
                            }
                            audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                        });
                    if (denied is { } reason)
                    {
                        audit.Write(entry.WithOutcome(SecurityAuditOutcome.Denied, reason.ToString().ToLowerInvariant()));
                        return false;
                    }
                    return true;
                }
            }, token).ConfigureAwait(false);
            if (accepted)
            {
                lock (gate)
                {
                    if (!eligible() || !candidate.Eligible())
                    { throw new InvalidOperationException("Original conversation execution preference admission changed before confirmation."); }
                    RequireReadback();
                    preferences.ConfirmWrite();
                    try
                    {
                        if (preferences.Load() != candidate.Proposed)
                        { throw new InvalidDataException("Confirmed conversation execution preference does not match."); }
                    }
                    catch { preferences.BeginWrite(); throw; }
                    saved = candidate.Proposed;
                    budget = new(saved, checked(++revision), true);
                    applying = false;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            return accepted;
        }
        catch { HoldUnavailable(); throw; }
        finally { if (entered) { lock (gate) { applying = false; } } }

        void RequireReadback()
        {
            if (preferences.ReadBack() != candidate.Proposed)
            { throw new InvalidDataException("Conversation execution preference readback does not match."); }
        }
    }

    public void HoldUnavailable()
    {
        lock (gate)
        {
            budget = null;
            review = null;
            revision = checked(revision + 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
