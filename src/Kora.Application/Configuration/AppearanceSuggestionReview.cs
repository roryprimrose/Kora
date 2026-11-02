using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Application.Communication;

namespace Kora.Application.Configuration;

/// <summary>Inert answer data reviewed by the original local user; never model write authority.</summary>
public sealed class AppearanceSuggestionReview : IDisposable
{
    private readonly AppearanceConfigurationService configuration;
    private readonly HostRequest reviewRequest;
    private readonly HostRequest sourceRequest;
    private readonly Lock gate = new();
    private readonly CallCommunicationPolicy? policy;
    private readonly TimeProvider time;
    private readonly long callRevision;
    private readonly RequestOrigin sourceOrigin;
    private readonly DateTimeOffset expires;
    private readonly System.Diagnostics.ActivityContext reviewCause;
    private Func<bool> eligible;
    private Func<CancellationToken, Task>? validateSource;
    private AppearanceOptionState? current;
    private AppearanceValue? proposed;
    private bool consumed;
    private volatile bool retired;

    private AppearanceSuggestionReview(AppearanceConfigurationService configuration, AppearanceOptionState current,
        AppearanceValue proposed, HostRequest reviewRequest, HostRequest sourceRequest,
        Func<bool> eligible, Func<CancellationToken, Task> validateSource,
        CallCommunicationPolicy? policy, TimeProvider time, long callRevision)
    {
        this.configuration = configuration;
        this.current = current;
        this.proposed = proposed;
        this.reviewRequest = reviewRequest;
        this.sourceRequest = sourceRequest;
        this.eligible = eligible;
        this.validateSource = validateSource;
        this.policy = current.Descriptor.Option == AppearanceOption.ResponseTimeout ? policy : null;
        this.time = time;
        this.callRevision = callRevision;
        sourceOrigin = sourceRequest.Origin == RequestOrigin.ActivatedVoice ? sourceRequest.Origin : reviewRequest.Origin;
        expires = this.policy is null ? DateTimeOffset.MaxValue : time.GetUtcNow().AddSeconds(60);
        reviewCause = HostActivity.RequireCurrent().Activity!.Context;
    }

    public bool Available { get { lock (gate) { return !consumed && Current() && configuration.Get(current!.Descriptor.Option) == current; } } }
    public string Description
    {
        get
        {
            lock (gate)
            {
                return Available
                    ? $"{current!.Descriptor.Description}: current {AppearanceCommand.Format(current.Value)}"
                        + $" ({(current.IsSaved ? "saved" : "default")}); proposed {AppearanceCommand.Format(proposed!)}"
                        + $" {current.Descriptor.Units}. Scope: shared device-local appearance, not just this conversation. "
                        + "The assistant suggestion has changed nothing. Confirm saves only this exact option; Cancel changes nothing."
                        + (policy is null ? string.Empty
                            : " Response-window inactivity hide delay only, not a provider deadline. Always show and required questions/approvals remain unchanged. Review expires in 60 seconds.")
                    : string.Empty;
            }
        }
    }

    public static Task<AppearanceSuggestionReview> ReviewAsync(AppearanceConfigurationService configuration,
        ConversationMessage message, Func<bool> eligible, Func<CancellationToken, Task> validateSource, CancellationToken token)
        => ReviewCoreAsync(configuration, message, eligible, validateSource, null, TimeProvider.System, token);

    public static Task<AppearanceSuggestionReview> ReviewAsync(AppearanceConfigurationService configuration,
        CallCommunicationPolicy policy, TimeProvider time, ConversationMessage message, Func<bool> eligible,
        Func<CancellationToken, Task> validateSource, CancellationToken token) =>
        ReviewCoreAsync(configuration, message, eligible, validateSource, policy, time, token);

    private static async Task<AppearanceSuggestionReview> ReviewCoreAsync(AppearanceConfigurationService configuration,
        ConversationMessage message, Func<bool> eligible, Func<CancellationToken, Task> validateSource,
        CallCommunicationPolicy? policy, TimeProvider time, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(eligible);
        ArgumentNullException.ThrowIfNull(validateSource);
        message.Validate();
        if (RegisteredSettingSuggestions.Parse(message.AssistantResponse?.Body ?? string.Empty) is not RegisteredSettingProposal.Appearance proposal)
        { throw new InvalidDataException("No bounded appearance suggestion: request one exact registered appearance setting suggestion."); }
        var nativeOnly = proposal.Option != AppearanceOption.ResponseTimeout || policy is null;
        var request = RequireInput(message.Request.SessionId, nativeOnly);
        var call = policy?.Current.Revision ?? 0;
        // A UI click cannot relabel an original protected/voice request as a native local request.
        if (request.RequestId == message.Request.RequestId || nativeOnly && message.Request.Origin != RequestOrigin.LocalUi
            || message.Outcome != ModelTurnOutcome.Succeeded
            || message.AssistantResponse is null)
        { throw new InvalidOperationException("Appearance review requires the original local UI conversation's admitted successful answer."); }
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("The original private appearance review source is no longer eligible."); }
        await validateSource(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!eligible() || RequireInput(message.Request.SessionId, nativeOnly) != request)
        { throw new InvalidOperationException("The original appearance review input changed."); }
        var state = configuration.Get(proposal.Option);
        var origin = message.Request.Origin == RequestOrigin.ActivatedVoice ? message.Request.Origin : request.Origin;
        if (!nativeOnly && policy!.CheckMutation(origin, call, eligible) is not null)
        { throw new InvalidOperationException("The original settings channel or call policy denies this review."); }
        return new(configuration, state, proposal.Value, request, message.Request, eligible, validateSource, policy, time, call);
    }

    public async Task<AppearanceApplyResult> ConfirmAsync(CancellationToken token,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        var request = RequireInput(reviewRequest.SessionId, policy is null);
        if (request.RequestId == reviewRequest.RequestId || request.RequestId == sourceRequest.RequestId)
        { throw new InvalidOperationException("Appearance confirmation requires a separate fresh local user request."); }
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || policy is null && initiator != SecurityAuditInitiator.LocalUser
            || initiator == SecurityAuditInitiator.VoiceCommand && request.Origin != RequestOrigin.ActivatedVoice)
        { throw new InvalidOperationException("Settings confirmation requires fresh original-user authority."); }
        AppearanceOptionState state;
        AppearanceValue value;
        Func<CancellationToken, Task> validate;
        lock (gate)
        {
            if (consumed || !Current())
            { throw new InvalidOperationException("Appearance review expired. Review a fresh suggestion; nothing was applied."); }
            consumed = true;
            state = current!;
            value = proposed!;
            validate = validateSource!;
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        activity.Activity!.AddLink(new(reviewCause));
        try
        {
            token.ThrowIfCancellationRequested();
            await validate(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Current() || RequireInput(reviewRequest.SessionId, policy is null) != request)
            { throw new InvalidOperationException("The original appearance source or native confirmation admission changed."); }
            var origin = sourceOrigin == RequestOrigin.ActivatedVoice ? sourceOrigin : request.Origin;
            bool Eligible() => Current() && !token.IsCancellationRequested;
            var proposal = configuration.Propose(state.Descriptor.Option, value, state.Revision,
                origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator, origin, Eligible);
            var result = policy is null
                ? await configuration.ApplyAsync(proposal, token).ConfigureAwait(false)
                : await configuration.ApplyAsync(proposal, policy, callRevision, token).ConfigureAwait(false);
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

    private static HostRequest RequireInput(HostId<SessionIdentity> session, bool nativeOnly)
    {
        var activity = HostActivity.RequireCurrent();
        if (activity.Activity!.IsStopped || activity.Outcome != HostOperationOutcome.Unknown
            || (nativeOnly ? activity.Request.Origin != RequestOrigin.LocalUi
                : activity.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
            || activity.Request.SessionId != session
            || activity.Request.InvocationId is not null)
        { throw new InvalidOperationException("Appearance review requires fresh original-user input in the exact local session."); }
        return activity.Request;
    }

    private bool Current() => !retired && eligible() && time.GetUtcNow() < expires
        && (policy is null || policy.CheckMutation(sourceOrigin, callRevision, eligible) is null);

    public void Dispose()
    {
        lock (gate)
        {
            retired = true;
            consumed = true;
            current = null;
            proposed = null;
            validateSource = null;
            eligible = static () => false;
        }
    }
}
