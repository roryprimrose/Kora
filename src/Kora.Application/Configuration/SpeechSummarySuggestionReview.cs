using System.Globalization;
using System.Diagnostics;

using Kora.Application.Communication;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

/// <summary>One memory-only review of inert retained answer data, never model approval authority.</summary>
public sealed class SpeechSummarySuggestionReview : IDisposable
{
    public static string ModelPolicy =>
        "For a requested future ordinary spoken-response limit suggestion only, the entire final answer may instead be "
        + $"one inert suggestion: 'set speech.summary-words to <integer>' (1-{SpokenSummaryLimits.MaximumWords}) or "
        + $"'set speech.summary-sentences to <integer>' (1-{SpokenSummaryLimits.MaximumSentences}). Use canonical decimal digits. "
        + "No other speech setting, reset, batch or command is admitted. Nothing is applied or spoken. "
        + "The original user must review the complete current/proposed shared device-local limits and separately confirm; "
        + "the current answer is never truncated, regenerated or replayed.";

    private readonly SpeechConfigurationService configuration;
    private readonly CallCommunicationPolicy policy;
    private readonly TimeProvider time;
    private readonly DateTimeOffset expires;
    private readonly SpeechConfigurationState current;
    private readonly SpeechOptionDescriptor descriptor;
    private readonly string value;
    private readonly SpokenSummaryLimits proposed;
    private readonly HostRequest reviewRequest;
    private readonly RequestOrigin sourceOrigin;
    private readonly long callRevision;
    private readonly ActivityLink reviewCause;
    private readonly ModelTurnProvenance source;
    private readonly Lock gate = new();
    private Func<bool> eligible;
    private Func<CancellationToken, Task>? validateSource;
    private bool consumed;
    private volatile bool retired;

    private SpeechSummarySuggestionReview(SpeechConfigurationService configuration, CallCommunicationPolicy policy,
        TimeProvider time, SpeechConfigurationState current, SpeechOptionDescriptor descriptor, string value,
        SpokenSummaryLimits proposed, HostRequest reviewRequest, RequestOrigin sourceOrigin, long callRevision, Func<bool> eligible,
        Func<CancellationToken, Task> validateSource, ModelTurnProvenance source)
    {
        this.configuration = configuration;
        this.policy = policy;
        this.time = time;
        expires = time.GetUtcNow().AddSeconds(60);
        this.current = current;
        this.descriptor = descriptor;
        this.value = value;
        this.proposed = proposed;
        this.reviewRequest = reviewRequest;
        this.sourceOrigin = sourceOrigin;
        this.eligible = eligible;
        this.validateSource = validateSource;
        this.callRevision = callRevision;
        reviewCause = new(HostActivity.RequireCurrent().Activity!.Context);
        this.source = source;
    }

    public bool Available { get { lock (gate) { return !consumed && Current(); } } }
    public string Description => Available
        ? $"Current: {current.SummaryLimits!.Sentences} sentences, {current.SummaryLimits.Words} words "
            + $"({(current.AreSummaryLimitsSaved ? "saved" : "default")}). "
            + $"Proposed: {proposed.Sentences} sentences, {proposed.Words} words. "
            + $"Only {descriptor.Id} is proposed; the companion cap is unchanged. Scope: shared device-local, not session-only. "
            + "Applies to NEXT ordinary speech only; this answer is not truncated, regenerated, spoken or replayed. "
            + "Nothing has changed. Confirm exact speech limit change separately, or Cancel. Review expires in 60 seconds. "
            + (current.Recovery ?? "Visual results remain available.")
        : string.Empty;

    public static async Task<SpeechSummarySuggestionReview> ReviewAsync(SpeechConfigurationService configuration,
        CallCommunicationPolicy policy, TimeProvider time, ConversationMessage message, Func<bool> eligible,
        Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Validate();
        var request = RequireInput(message.Request.SessionId);
        var observedCall = policy.Current.Revision;
        if (request.RequestId == message.Request.RequestId || message.Outcome != ModelTurnOutcome.Succeeded
            || message.AssistantResponse is not { } answer)
        { throw new InvalidOperationException("Review requires an exact successful retained original-user answer, not model authority."); }
        var command = SpeechCommand.Parse(answer.Body, AssistantNameRules.DefaultName);
        if (command is not { Operation: AppearanceCommandOperation.Set, Descriptor.IsSummaryLimit: true, Value: not null }
            || !string.Equals(answer.Body.Trim(), $"set {command.Descriptor.Id} to {command.Value}", StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Request one exact speech.summary-words or speech.summary-sentences Set suggestion only."); }
        var state = configuration.Get();
        var limits = state.SummaryLimits
            ?? throw new InvalidOperationException("Repair unreadable saved summary limits and refresh; the companion cap cannot be defaulted.");
        var next = limits.With(command.Descriptor.Option, command.Value);
        var number = command.Descriptor.Option == SpeechOption.SummaryWords ? next.Words : next.Sentences;
        if (!string.Equals(command.Value, number.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        { throw new InvalidDataException("A speech limit suggestion requires one canonical decimal integer."); }
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("The original speech limit source is no longer eligible."); }
        await validateSource(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!eligible() || RequireInput(message.Request.SessionId) != request || configuration.Get().Revision != state.Revision)
        { throw new InvalidOperationException("The original speech limit review input or settings changed."); }
        var origin = message.Request.Origin == RequestOrigin.ActivatedVoice ? message.Request.Origin : request.Origin;
        if (policy.CheckMutation(origin, observedCall, eligible) is not null)
        { throw new InvalidOperationException("The original channel or call policy denies this speech limit review. Start a genuinely new eligible input."); }
        return new(configuration, policy, time, state, command.Descriptor, command.Value, next,
            request, origin, observedCall, eligible, validateSource, message.Execution!);
    }

    public async Task<SpeechApplyResult> ConfirmAsync(CancellationToken token,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        var request = RequireInput(reviewRequest.SessionId);
        if (request.RequestId == reviewRequest.RequestId || initiator is not
            (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand))
        { throw new InvalidOperationException("Confirmation requires separate fresh original-user input."); }
        Func<CancellationToken, Task> validate;
        lock (gate)
        {
            if (!Available) { throw new InvalidOperationException("Speech limit review expired. Review the exact source again; nothing was applied."); }
            consumed = true;
            validate = validateSource!;
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        activity.Activity!.AddLink(reviewCause);
        activity.Activity.SetTag("kora.source.request.id", source.Request.RequestId.Value);
        activity.Activity.SetTag("kora.source.task.id", source.Request.TaskId.Value);
        activity.Activity.SetTag("kora.source.turn.id", source.Turn.Value);
        activity.Activity.SetTag("kora.source.origin", sourceOrigin.ToString());
        try
        {
            token.ThrowIfCancellationRequested();
            await validate(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Current() || RequireInput(reviewRequest.SessionId) != request)
            { throw new InvalidOperationException("Speech limit source or original confirmation admission changed."); }
            var voice = sourceOrigin == RequestOrigin.ActivatedVoice || request.Origin == RequestOrigin.ActivatedVoice;
            var proposal = configuration.Propose(descriptor.Option, value, current.Revision, callRevision,
                voice ? RequestOrigin.ActivatedVoice : request.Origin,
                voice ? SecurityAuditInitiator.VoiceCommand : initiator);
            bool ConfirmationCurrent() => Current() && HostActivity.Current is { } live
                && !live.Activity!.IsStopped && live.Outcome == HostOperationOutcome.Unknown && live.Request == request;
            var result = configuration.Apply(proposal, policy, ConfirmationCurrent, token);
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

    private static HostRequest RequireInput(HostId<SessionIdentity> session)
    {
        var activity = HostActivity.RequireCurrent();
        if (activity.Activity!.IsStopped || activity.Outcome != HostOperationOutcome.Unknown
            || activity.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || activity.Request.SessionId != session || activity.Request.InvocationId is not null)
        { throw new InvalidOperationException("Speech limit review requires live original-user input in the exact session."); }
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
