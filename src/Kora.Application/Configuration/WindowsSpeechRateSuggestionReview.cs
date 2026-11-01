using System.Diagnostics;
using System.Globalization;

using Kora.Application.Communication;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

/// <summary>One inert retained-answer review; the existing Windows rate writer alone owns mutation.</summary>
public sealed class WindowsSpeechRateSuggestionReview : IDisposable
{
    private readonly WindowsSpeechRateConfigurationService configuration;
    private readonly CallCommunicationPolicy policy;
    private readonly TimeProvider time;
    private readonly WindowsSpeechRateState current;
    private readonly WindowsSpeechRate proposed;
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

    private WindowsSpeechRateSuggestionReview(WindowsSpeechRateConfigurationService configuration,
        CallCommunicationPolicy policy, TimeProvider time, WindowsSpeechRateState current, WindowsSpeechRate proposed,
        HostRequest reviewRequest, RequestOrigin sourceOrigin, long callRevision, Func<bool> eligible,
        Func<CancellationToken, Task> validateSource, ModelTurnProvenance source)
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
    internal bool BelongsTo(WindowsSpeechRateConfigurationService owner) => ReferenceEquals(configuration, owner);
    public string Description => Available
        ? $"Current desired: {current.Desired!.Value.Value}; effective: {current.Effective!.Value.Value} ({current.Source}). "
            + $"Proposed: {proposed.Value}. Provider: {current.ProviderId}; support: {current.Support}. "
            + "Units: Windows native -10 through 10, normal 0, not percent or a common speed scale. "
            + "Scope: shared device-local, Kora's Windows synthesizer only; Kokoro unchanged. "
            + "Nothing has changed. Applies to NEXT eligible Windows speech only; this answer is never synthesized or replayed. "
            + "Confirm exact setting change separately, or Cancel. Review expires in 60 seconds. Full visual output remains available."
        : string.Empty;

    public static async Task<WindowsSpeechRateSuggestionReview> ReviewAsync(WindowsSpeechRateConfigurationService configuration,
        CallCommunicationPolicy policy, TimeProvider time, ConversationMessage message, Func<bool> eligible,
        Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Validate();
        var request = RequireInput(message.Request.SessionId);
        var call = policy.Current.Revision;
        if (request.RequestId == message.Request.RequestId || message.Outcome != ModelTurnOutcome.Succeeded)
        { throw new InvalidOperationException("Review requires an exact successful retained original-user answer, not model authority."); }
        if (RegisteredSettingSuggestions.Parse(message.AssistantResponse!.Body) is not RegisteredSettingProposal.WindowsRate proposal)
        { throw new InvalidDataException("Request one exact speech.windows-rate Set suggestion only."); }
        var state = configuration.Get();
        if (!state.Available)
        { throw new InvalidOperationException("Windows rate is unavailable. Inspect Settings recovery and explicitly refresh before a fresh review."); }
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("The original rate suggestion source is no longer eligible."); }
        await validateSource(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!eligible() || RequireInput(message.Request.SessionId) != request || configuration.Get() != state)
        { throw new InvalidOperationException("The original rate review input or settings changed."); }
        var origin = message.Request.Origin == RequestOrigin.ActivatedVoice ? message.Request.Origin : request.Origin;
        if (policy.CheckMutation(origin, call, eligible) is not null)
        { throw new InvalidOperationException("The original channel or call policy denies this rate review."); }
        return new(configuration, policy, time, state, proposal.Value, request, origin, call, eligible, validateSource, message.Execution!);
    }

    public async Task<bool> ConfirmAsync(CancellationToken token, SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        var request = RequireInput(reviewRequest.SessionId);
        var confirmation = HostActivity.RequireCurrent();
        if (request.RequestId == reviewRequest.RequestId || request.RequestId == source.Request.RequestId || initiator is not
            (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand))
        { throw new InvalidOperationException("Confirmation requires separate fresh original-user input."); }
        Func<CancellationToken, Task> validate;
        lock (gate)
        {
            if (!Available) { throw new InvalidOperationException("Rate review expired or changed. Review the exact source again; nothing was applied."); }
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
            if (!Current() || RequireInput(reviewRequest.SessionId) != request)
            { throw new InvalidOperationException("Rate source or original confirmation admission changed."); }
            var origin = sourceOrigin == RequestOrigin.ActivatedVoice ? sourceOrigin : request.Origin;
            bool Eligible() => SourceCurrent() && !token.IsCancellationRequested
                && !confirmation.Activity!.IsStopped && confirmation.Outcome == HostOperationOutcome.Unknown;
            await configuration.RefreshAsync(origin, Eligible, token, current.Revision).ConfigureAwait(false);
            var refreshed = configuration.Get();
            if (!Eligible() || refreshed != (current with { Revision = refreshed.Revision }))
            { throw new InvalidOperationException("The reviewed rate or provider changed during fresh audio admission."); }
            await validate(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Eligible() || RequireInput(reviewRequest.SessionId) != request || configuration.Get() != refreshed)
            { throw new InvalidOperationException("The exact rate source, confirmation or refreshed settings changed before apply."); }
            var candidate = configuration.Propose(proposed.Value.ToString(CultureInfo.InvariantCulture), refreshed.Revision, callRevision);
            var saved = await configuration.ApplyAsync(candidate, origin,
                origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                policy, Eligible, token).ConfigureAwait(false);
            activity.Complete(saved ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return saved;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        finally { Dispose(); }
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
        { throw new InvalidOperationException("Rate review requires live original-user input in the exact session."); }
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
