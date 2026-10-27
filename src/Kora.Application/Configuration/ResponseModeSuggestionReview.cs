using System.Diagnostics;

using Kora.Application.Communication;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

/// <summary>One inert retained-answer review, consumed only by fresh original-user confirmation.</summary>
public sealed class ResponseModeSuggestionReview : IDisposable
{
    private readonly ResponseModeConfigurationService configuration;
    private readonly CallCommunicationPolicy policy;
    private readonly TimeProvider time;
    private readonly ResponseModeCommandResult current;
    private readonly ResponseOutputMode proposed;
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

    private ResponseModeSuggestionReview(ResponseModeConfigurationService configuration,
        CallCommunicationPolicy policy, TimeProvider time, ResponseModeCommandResult current,
        ResponseOutputMode proposed, HostRequest reviewRequest, RequestOrigin sourceOrigin,
        long callRevision, Func<bool> eligible, Func<CancellationToken, Task> validateSource, ModelTurnProvenance source)
    {
        this.configuration = configuration;
        this.policy = policy;
        this.time = time;
        this.current = current;
        this.proposed = proposed;
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
    internal bool BelongsTo(ResponseModeConfigurationService owner) => ReferenceEquals(configuration, owner);
    public string Description => Available
        ? $"Current saved: {current.Saved?.ToString() ?? "unsaved"} ({current.Source}); desired: {current.Desired}; "
            + $"configured effective: {current.Effective}. Proposed: {proposed}. Scope: shared device-local default. "
            + "Nothing has changed. Applies to NEXT eligible response only; no synthesis, capture, regeneration or replay of this answer. "
            + "Session/task/queue and in-call overrides remain unchanged. Mandatory full visual warnings, security, questions, approvals "
            + "and speech-unavailable fallback remain, even in VoiceOnly. Privacy and call restrictions always win. "
            + "Confirm this exact change separately, or Cancel. Review expires in 60 seconds."
        : string.Empty;

    public static async Task<ResponseModeSuggestionReview> ReviewAsync(ResponseModeConfigurationService configuration,
        CallCommunicationPolicy policy, TimeProvider time, ConversationMessage message, Func<bool> eligible,
        Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Validate();
        var request = RequireInput(message.Request.SessionId);
        var call = policy.Current.Revision;
        if (request.RequestId == message.Request.RequestId || message.Outcome != ModelTurnOutcome.Succeeded)
        { throw new InvalidOperationException("Review requires an exact successful retained original-user answer, not model authority."); }
        if (RegisteredSettingSuggestions.Parse(message.AssistantResponse!.Body) is not RegisteredSettingProposal.ResponseMode proposal)
        { throw new InvalidDataException("Request one exact responses.default-mode Set suggestion only."); }
        var state = configuration.Get();
        if (!state.Available)
        { throw new InvalidOperationException("Response mode is unavailable. Inspect Settings recovery and explicitly refresh before a fresh review."); }
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("The original response-mode suggestion source is no longer eligible."); }
        await validateSource(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!eligible() || RequireInput(message.Request.SessionId) != request || configuration.Get() != state)
        { throw new InvalidOperationException("The original response-mode review input or settings changed."); }
        var origin = message.Request.Origin == RequestOrigin.ActivatedVoice ? message.Request.Origin : request.Origin;
        if (policy.CheckMutation(origin, call, eligible) is not null)
        { throw new InvalidOperationException("The original channel or call policy denies this response-mode review."); }
        return new(configuration, policy, time, state, proposal.Value, request, origin, call, eligible, validateSource, message.Execution!);
    }

    public async Task<bool> ConfirmAsync(CancellationToken token, SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        var request = RequireInput(reviewRequest.SessionId);
        var confirmation = HostActivity.RequireCurrent();
        if (request.RequestId == reviewRequest.RequestId || request.RequestId == source.Request.RequestId || initiator is not
            (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && request.Origin != RequestOrigin.ActivatedVoice)
        { throw new InvalidOperationException("Confirmation requires separate fresh original-user input."); }
        Func<CancellationToken, Task> validate;
        lock (gate)
        {
            if (!Available) { throw new InvalidOperationException("Response-mode review expired or changed. Review the exact source again; nothing was applied."); }
            consumed = true;
            validate = validateSource!;
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        activity.Activity!.AddLink(reviewCause);
        activity.Activity.SetTag("kora.source.request.id", source.Request.RequestId.Value);
        activity.Activity.SetTag("kora.source.task.id", source.Request.TaskId.Value);
        activity.Activity.SetTag("kora.source.turn.id", source.Turn.Value);
        activity.Activity.SetTag("kora.source.origin", source.Request.Origin.ToString());
        HostActivity? channel = null;
        try
        {
            token.ThrowIfCancellationRequested();
            await validate(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Current() || RequireInput(reviewRequest.SessionId) != request)
            { throw new InvalidOperationException("Response-mode source or original confirmation admission changed."); }
            var origin = sourceOrigin == RequestOrigin.ActivatedVoice ? sourceOrigin : request.Origin;
            // Audio admission captures its ambient input. Preserve the most restrictive original channel,
            // linked to fresh confirmation, rather than relabelling a voice source as UI authority.
            channel = HostActivity.BeginRoot(new(new(Guid.NewGuid()), reviewRequest.SessionId,
                new(Guid.NewGuid()), origin), HostActivityLayer.Application, HostOperation.Policy,
                [new(activity.Activity.Context)]);
            // Fresh audio admission owns a separate session. Its writer revalidates the choice and readback.
            bool Eligible() => SourceCurrent() && !token.IsCancellationRequested
                && !confirmation.Activity!.IsStopped && confirmation.Outcome == HostOperationOutcome.Unknown;
            await configuration.RefreshAsync(origin, Eligible, token, current.Revision).ConfigureAwait(false);
            var refreshed = configuration.Get();
            if (!Eligible() || refreshed != (current with { Revision = refreshed.Revision }))
            { throw new InvalidOperationException("The reviewed response preference changed during fresh audio admission."); }
            await validate(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Eligible() || RequireInput(reviewRequest.SessionId) != channel.Request || configuration.Get() != refreshed)
            { throw new InvalidOperationException("The exact source, confirmation or refreshed response settings changed before apply."); }
            var choice = configuration.Choices.Single(item => item.Mode == proposed);
            var saved = await configuration.SelectAsync(choice, callRevision, origin,
                origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                policy, Eligible, token).ConfigureAwait(false);
            activity.Complete(saved ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            channel.Complete(saved ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return saved;
        }
        catch (OperationCanceledException)
        {
            channel?.Complete(HostOperationOutcome.Cancelled);
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            channel?.Complete(HostOperationOutcome.Failed);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally { channel?.Dispose(); Dispose(); }
    }

    private bool SourceCurrent() => !retired && time.GetUtcNow() < expires && eligible()
        && policy.Current.Revision == callRevision;
    private bool Current() => SourceCurrent() && configuration.Get() == current;

    private static HostRequest RequireInput(HostId<SessionIdentity> session)
    {
        var activity = HostActivity.RequireCurrent();
        if (activity.Activity!.IsStopped || activity.Outcome != HostOperationOutcome.Unknown
            || activity.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || activity.Request.SessionId != session || activity.Request.InvocationId is not null)
        { throw new InvalidOperationException("Response-mode review requires live original-user input in the exact session."); }
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
