using System.Diagnostics;

using Kora.Application.Communication;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

/// <summary>One retained-source and installed-catalogue review; only the existing speech writer mutates preferences.</summary>
public sealed class InstalledSpeechVoiceSuggestionReview : IDisposable
{
    private readonly SpeechConfigurationService configuration;
    private readonly CallCommunicationPolicy policy;
    private readonly TimeProvider time;
    private readonly SpeechConfigurationState current;
    private readonly SpeechSelection proposed;
    private readonly SpeechVoice proposedVoice;
    private readonly HostRequest reviewRequest;
    private readonly RequestOrigin sourceOrigin;
    private readonly long callRevision;
    private readonly DateTimeOffset expires;
    private readonly ActivityLink reviewCause;
    private readonly ModelTurnProvenance source;
    private readonly Lock gate = new();
    private Func<bool> eligible;
    private Func<CancellationToken, Task>? validateSource;
    private bool consumed;
    private volatile bool retired;

    private InstalledSpeechVoiceSuggestionReview(SpeechConfigurationService configuration, CallCommunicationPolicy policy,
        TimeProvider time, SpeechConfigurationState current, SpeechSelection proposed, SpeechVoice proposedVoice,
        HostRequest reviewRequest, RequestOrigin sourceOrigin, long callRevision, Func<bool> eligible,
        Func<CancellationToken, Task> validateSource, ModelTurnProvenance source)
    {
        this.configuration = configuration;
        this.policy = policy;
        this.time = time;
        this.current = current;
        this.proposed = proposed;
        this.proposedVoice = proposedVoice;
        this.reviewRequest = reviewRequest;
        this.sourceOrigin = sourceOrigin;
        this.callRevision = callRevision;
        this.eligible = eligible;
        this.validateSource = validateSource;
        this.source = source;
        expires = time.GetUtcNow().AddSeconds(60);
        reviewCause = new(HostActivity.RequireCurrent().Activity!.Context);
    }

    public bool Available { get { lock (gate) { return !consumed && Current(); } } }
    public string Description => Available
        ? $"Current provider: {current.Selection!.ProviderId}; voice: {current.Selection.VoiceId ?? "provider default"} "
            + $"({(current.IsSaved ? "saved" : "default")}); effective: {current.EffectiveVoice?.ConfigurationId ?? "unavailable"}. "
            + $"Proposed provider: {proposed.ProviderId}; voice: {proposed.VoiceId} ({proposedVoice.Name}). "
            + "Source: exact successful retained answer; installed catalogue and configuration are bound to this review. "
            + "Scope: shared device-local, not session-only. Both provider and voice are saved atomically. "
            + "Nothing has changed. Applies to NEXT separately requested eligible ordinary speech only; "
            + "this answer is never synthesized, regenerated or replayed. No assets are downloaded or installed. "
            + "Confirm exact installed voice change separately, or Cancel. Review expires in 60 seconds. "
            + (current.Recovery ?? "Full visual output remains available.")
        : string.Empty;

    public static async Task<InstalledSpeechVoiceSuggestionReview> ReviewAsync(SpeechConfigurationService configuration,
        CallCommunicationPolicy policy, TimeProvider time, ConversationMessage message, Func<bool> eligible,
        Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Validate();
        var request = RequireInput(message.Request.SessionId);
        var call = policy.Current.Revision;
        if (request.RequestId == message.Request.RequestId || message.Outcome != ModelTurnOutcome.Succeeded)
        { throw new InvalidOperationException("Review requires an exact successful retained original-user answer, not model authority."); }
        if (RegisteredSettingSuggestions.Parse(message.AssistantResponse!.Body) is not RegisteredSettingProposal.InstalledVoice proposal)
        { throw new InvalidDataException("Request one exact qualified speech.voice Set suggestion only."); }
        var state = configuration.Get();
        token.ThrowIfCancellationRequested();
        if (!eligible() || state.Selection is null || !configuration.IsCurrentSelection(state.Revision))
        { throw new InvalidOperationException("The installed voice source or configuration is unavailable or changed. Inspect Settings and refresh."); }
        var origin = message.Request.Origin == RequestOrigin.ActivatedVoice ? message.Request.Origin : request.Origin;
        var resolved = configuration.Propose(SpeechOption.Voice, $"{proposal.Value.ProviderId} / {proposal.Value.VoiceId}",
            state.Revision, call, origin, SecurityAuditInitiator.LocalUser);
        if (resolved.Selection != proposal.Value)
        { throw new InvalidOperationException("The exact installed provider and voice pair could not be resolved."); }
        var voice = state.Voices.Single(item => string.Equals(item.ProviderId, proposal.Value.ProviderId, StringComparison.Ordinal)
            && string.Equals(item.Id, proposal.Value.VoiceId, StringComparison.Ordinal));
        await validateSource(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!eligible() || RequireInput(message.Request.SessionId) != request || !configuration.IsCurrentSelection(state.Revision))
        { throw new InvalidOperationException("The exact voice source, installed catalogue or review input changed."); }
        if (policy.CheckMutation(origin, call, eligible) is not null)
        { throw new InvalidOperationException("The original channel or call policy denies this installed voice review."); }
        return new(configuration, policy, time, state, proposal.Value, voice, request, origin, call, eligible, validateSource, message.Execution!);
    }

    public async Task<SpeechApplyResult> ConfirmAsync(CancellationToken token,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        var request = RequireInput(reviewRequest.SessionId);
        var confirmation = HostActivity.RequireCurrent();
        if (request.RequestId == reviewRequest.RequestId || request.RequestId == source.Request.RequestId || initiator is not
            (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand))
        { throw new InvalidOperationException("Confirmation requires separate fresh original-user input."); }
        Func<CancellationToken, Task> validate;
        lock (gate)
        {
            if (!Available) { throw new InvalidOperationException("Installed voice review expired or changed. Review the exact source again; nothing was applied."); }
            consumed = true;
            validate = validateSource!;
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        activity.Activity!.AddLink(reviewCause);
        activity.Activity.SetTag("kora.source.request.id", source.Request.RequestId.Value);
        activity.Activity.SetTag("kora.source.task.id", source.Request.TaskId.Value);
        activity.Activity.SetTag("kora.source.turn.id", source.Turn.Value);
        activity.Activity.SetTag("kora.source.origin", source.Request.Origin.ToString());
        try
        {
            token.ThrowIfCancellationRequested();
            await validate(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Current() || RequireInput(reviewRequest.SessionId) != request || !configuration.IsCurrentSelection(current.Revision))
            { throw new InvalidOperationException("The reviewed voice source, configuration or installed catalogue changed."); }
            var voiceOrigin = sourceOrigin == RequestOrigin.ActivatedVoice || request.Origin == RequestOrigin.ActivatedVoice;
            var proposal = configuration.Propose(SpeechOption.Voice, proposedVoice.ConfigurationId, current.Revision, callRevision,
                voiceOrigin ? RequestOrigin.ActivatedVoice : request.Origin,
                voiceOrigin ? SecurityAuditInitiator.VoiceCommand : initiator);
            bool ConfirmationCurrent() => Current() && !confirmation.Activity!.IsStopped
                && confirmation.Outcome == HostOperationOutcome.Unknown
                && HostActivity.RequireCurrent().Request == request;
            var result = configuration.Apply(proposal, policy, ConfirmationCurrent, token);
            if (result.Succeeded) { RequireReadback(result.State.Revision); }
            activity.Complete(result.Succeeded ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally { Dispose(); }
    }

    private bool Current() => !retired && time.GetUtcNow() < expires && eligible()
        && configuration.Get().Revision == current.Revision && policy.Current.Revision == callRevision;

    private void RequireReadback(long revision)
    {
        const string recovery = "Installed voice save readback is unconfirmed. Inspect Settings and audit receipts; "
            + "bytes may have persisted. No automatic retry or rollback.";
        try
        {
            if (configuration.IsCurrentSelection(revision)) { return; }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            configuration.HoldUnavailable(recovery);
            throw new InvalidDataException(recovery, exception);
        }
        configuration.HoldUnavailable(recovery);
        throw new InvalidDataException(recovery);
    }

    private static HostRequest RequireInput(HostId<SessionIdentity> session)
    {
        var activity = HostActivity.RequireCurrent();
        if (activity.Activity!.IsStopped || activity.Outcome != HostOperationOutcome.Unknown
            || activity.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || activity.Request.SessionId != session || activity.Request.InvocationId is not null)
        { throw new InvalidOperationException("Installed voice review requires live original-user input in the exact session."); }
        return activity.Request;
    }

    public void Dispose()
    {
        lock (gate)
        {
            retired = true;
            consumed = true;
            eligible = static () => false;
            validateSource = null;
        }
    }
}
