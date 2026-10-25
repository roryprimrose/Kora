using System.Diagnostics;
using System.Globalization;

using Kora.Application.Communication;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

/// <summary>One inert exact retained-answer review; only the admitted volume writer can commit it.</summary>
public sealed class PlaybackVolumeSuggestionReview : IDisposable
{
    public static string ModelPolicy =>
        "For a requested Kora-only playback volume suggestion, the entire final answer may instead be exactly "
        + "'set speech.playback-volume to <canonical integer 0-100>'. This is one inert Set suggestion, "
        + "not approval. No reset, adjustment, batch, commentary, system/device/call volume or other setting. "
        + "The original user must review the exact retained answer and separately confirm the shared device-local change. "
        + "Never synthesize or replay this answer; zero blocks future synthesis and preserves full visual output.";

    private readonly PlaybackVolumeConfigurationService configuration;
    private readonly CallCommunicationPolicy policy;
    private readonly TimeProvider time;
    private readonly PlaybackVolumeState current;
    private readonly PlaybackVolume proposed;
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

    private PlaybackVolumeSuggestionReview(PlaybackVolumeConfigurationService configuration,
        CallCommunicationPolicy policy, TimeProvider time, PlaybackVolumeState current, PlaybackVolume proposed,
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
    internal bool BelongsTo(PlaybackVolumeConfigurationService owner) => ReferenceEquals(configuration, owner);
    public string Description => Available
        ? $"Current desired: {current.Desired!.Value.Percent}%; effective: {current.Effective!.Value.Percent}% ({current.Source}). "
            + $"Proposed: {proposed.Percent}%. Scope: shared device-local, Kora-owned speech only, not system/device/call volume. "
            + "Nothing has changed. Applies to NEXT eligible speech only; this answer is never synthesized or replayed. "
            + "Zero blocks future synthesis and automatic playback; full output remains visual. "
            + "Confirm exact volume change separately, or Cancel. Review expires in 60 seconds. "
            + (current.Recovery ?? "Full visual output remains available.")
        : string.Empty;

    public static async Task<PlaybackVolumeSuggestionReview> ReviewAsync(PlaybackVolumeConfigurationService configuration,
        CallCommunicationPolicy policy, TimeProvider time, ConversationMessage message, Func<bool> eligible,
        Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Validate();
        var request = RequireInput(message.Request.SessionId);
        var call = policy.Current.Revision;
        if (request.RequestId == message.Request.RequestId || message.Outcome != ModelTurnOutcome.Succeeded)
        { throw new InvalidOperationException("Review requires an exact successful retained original-user answer, not model authority."); }
        var answer = message.AssistantResponse!;
        var command = PlaybackVolumeCommand.Parse(answer.Body, AssistantNameRules.DefaultName);
        if (command is not { Operation: AppearanceCommandOperation.Set, Adjustment: null, Value: not null })
        { throw new InvalidDataException("Request one exact speech.playback-volume Set suggestion only."); }
        var value = PlaybackVolume.Parse(command.Value);
        if (!string.Equals(answer.Body, $"set {PlaybackVolumeCommand.OptionId} to {value.Percent.ToString(CultureInfo.InvariantCulture)}",
            StringComparison.Ordinal))
        { throw new InvalidDataException("A volume suggestion requires the exact single Set form and one canonical decimal integer."); }
        var state = configuration.Get();
        if (!state.Available)
        { throw new InvalidOperationException("Volume is unavailable. Inspect Settings recovery and explicitly refresh before a fresh review."); }
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("The original volume suggestion source is no longer eligible."); }
        await validateSource(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!eligible() || RequireInput(message.Request.SessionId) != request || configuration.Get() != state)
        { throw new InvalidOperationException("The original volume review input or settings changed."); }
        var origin = message.Request.Origin == RequestOrigin.ActivatedVoice ? message.Request.Origin : request.Origin;
        if (policy.CheckMutation(origin, call, eligible) is not null)
        { throw new InvalidOperationException("The original channel or call policy denies this volume review."); }
        return new(configuration, policy, time, state, value, request, origin, call, eligible, validateSource, message.Execution!);
    }

    public async Task<bool> ConfirmAsync(CancellationToken token, SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser)
    {
        var request = RequireInput(reviewRequest.SessionId);
        var confirmation = HostActivity.RequireCurrent();
        if (request.RequestId == reviewRequest.RequestId || initiator is not
            (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand))
        { throw new InvalidOperationException("Confirmation requires separate fresh original-user input."); }
        Func<CancellationToken, Task> validate;
        lock (gate)
        {
            if (!Available) { throw new InvalidOperationException("Volume review expired or changed. Review the exact source again; nothing was applied."); }
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
            { throw new InvalidOperationException("Volume source or original confirmation admission changed."); }
            var origin = sourceOrigin == RequestOrigin.ActivatedVoice ? sourceOrigin : request.Origin;
            // Audio admission owns its own session/request. Retain the original review's eligibility,
            // but let its writer own proposal revision checks and its post-commit readback.
            bool Eligible() => SourceCurrent() && !token.IsCancellationRequested
                && !confirmation.Activity!.IsStopped && confirmation.Outcome == HostOperationOutcome.Unknown;
            await configuration.RefreshAsync(origin, Eligible, token, current.Revision).ConfigureAwait(false);
            var refreshed = configuration.Get();
            if (!Eligible() || refreshed != (current with { Revision = refreshed.Revision }))
            { throw new InvalidOperationException("The reviewed volume preference changed during fresh audio admission."); }
            await validate(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Eligible() || RequireInput(reviewRequest.SessionId) != request || configuration.Get() != refreshed)
            { throw new InvalidOperationException("The exact volume source, confirmation or refreshed settings changed before apply."); }
            var proposal = configuration.Propose(proposed.Percent.ToString(CultureInfo.InvariantCulture), refreshed.Revision, callRevision);
            var saved = await configuration.ApplyAsync(proposal, origin,
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
        if (activity.Outcome != HostOperationOutcome.Unknown
            || activity.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || activity.Request.SessionId != session || activity.Request.InvocationId is not null)
        { throw new InvalidOperationException("Volume review requires live original-user input in the exact session."); }
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
