using System.Diagnostics;

using Kora.Application.Communication;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Auditing;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

/// <summary>Inert retained timing proposal with independent native confirmation and joined persistence.</summary>
public sealed class VoiceCaptureTimingSuggestionReview : IDisposable
{
    private readonly VoiceCaptureTimingConfigurationService configuration;
    private readonly CallCommunicationPolicy policy;
    private readonly TimeProvider time;
    private readonly (VoiceCaptureTiming? Timing, bool IsSaved, long Revision) current;
    private readonly VoiceCaptureTiming proposed;
    private readonly string settingId;
    private readonly bool reset;
    private readonly HostRequest reviewRequest;
    private readonly ModelTurnProvenance source;
    private readonly long callRevision;
    private readonly DateTimeOffset expires;
    private readonly ActivityLink cause;
    private readonly Func<bool> eligible;
    private readonly Func<CancellationToken, Task> validateSource;
    private readonly Lock gate = new();
    private bool consumed;
    private volatile bool retired;

    private VoiceCaptureTimingSuggestionReview(VoiceCaptureTimingConfigurationService configuration,
        CallCommunicationPolicy policy, TimeProvider time,
        (VoiceCaptureTiming? Timing, bool IsSaved, long Revision) current, VoiceCaptureTiming proposed,
        HostRequest request, ModelTurnProvenance source, long callRevision, RegisteredSettingProposal proposal,
        Func<bool> eligible, Func<CancellationToken, Task> validateSource)
    {
        this.configuration = configuration;
        this.policy = policy;
        this.time = time;
        this.current = current;
        this.proposed = proposed;
        settingId = proposal.Descriptor.Id;
        reset = proposal is RegisteredSettingProposal.Reset;
        reviewRequest = request;
        this.source = source;
        this.callRevision = callRevision;
        this.eligible = eligible;
        this.validateSource = validateSource;
        expires = time.GetUtcNow().AddSeconds(60);
        cause = new(HostActivity.RequireCurrent().Activity!.Context);
    }

    public bool Available { get { lock (gate) { return !consumed && Current(); } } }
    internal bool BelongsTo(VoiceCaptureTimingConfigurationService owner) => ReferenceEquals(configuration, owner);
    public string Description => Available
        ? $"{settingId}: Current ({(current.IsSaved ? "saved" : "default, not saved")}): {current.Timing!.Describe()}. "
            + $"Proposed (saved tuple): {proposed.Describe()}. Defaults: {VoiceCaptureTiming.Default.Describe()}. "
            + (reset ? "Reset restores only the selected field's default and preserves companion fields. "
                : "Set preserves both companion fields. ")
            + "Scope: shared device-local, future eligible captures only; an in-flight capture keeps its snapshot. "
            + "Saved intent is not profile readiness or microphone consent. Nothing has changed or opened the microphone. "
            + "Confirm exact setting change separately in the current native surface, or Cancel. Review expires in 60 seconds."
        : string.Empty;

    public static async Task<VoiceCaptureTimingSuggestionReview> ReviewAsync(
        VoiceCaptureTimingConfigurationService configuration, CallCommunicationPolicy policy, TimeProvider time,
        ConversationMessage message, Func<bool> eligible, Func<CancellationToken, Task> validateSource,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Validate();
        var request = RequireNativeInput(message.Request.SessionId);
        if (request.RequestId == message.Request.RequestId || message.Outcome != ModelTurnOutcome.Succeeded)
        { throw new InvalidOperationException("Review requires an exact successful retained answer and separate native input."); }
        var proposal = RegisteredSettingSuggestions.Parse(message.AssistantResponse!.Body);
        var snapshot = configuration.Get();
        if (snapshot.Timing is null)
        { throw new InvalidOperationException("Capture timing is unavailable. Inspect Settings and audit before explicit recovery."); }
        var candidate = proposal switch
        {
            RegisteredSettingProposal.CaptureTiming timing => snapshot.Timing.With(timing.Descriptor.Id, VoiceCaptureTiming.Format(timing.Seconds)),
            RegisteredSettingProposal.Reset { Setting: RegisteredSetting.CommandStartWait or RegisteredSetting.ReplyStartWait or RegisteredSetting.TrailingSilence } reset =>
                snapshot.Timing.With(reset.Descriptor.Id, null),
            _ => throw new InvalidDataException("Review one exact capture timing Set or Reset only."),
        };
        var call = policy.Current.Revision;
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("The capture timing source is no longer current."); }
        await validateSource(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!eligible() || RequireNativeInput(message.Request.SessionId) != request || configuration.Get() != snapshot
            || policy.CheckMutation(message.Request.Origin, call, eligible) is not null)
        { throw new InvalidOperationException("The exact source, timing, native input or call policy changed."); }
        return new(configuration, policy, time, snapshot, candidate, request, message.Execution!, call, proposal, eligible, validateSource);
    }

    public async Task<bool> ConfirmAsync(CancellationToken token)
    {
        var request = RequireNativeInput(reviewRequest.SessionId);
        var confirmation = HostActivity.RequireCurrent();
        if (request.RequestId == reviewRequest.RequestId || request.RequestId == source.Request.RequestId)
        { throw new InvalidOperationException("Confirmation requires separate fresh native input."); }
        lock (gate)
        {
            if (!Available) { throw new InvalidOperationException("Capture timing review expired or changed. Review the exact answer again."); }
            consumed = true;
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        activity.Activity!.AddLink(cause);
        activity.Activity.SetTag("kora.source.request.id", source.Request.RequestId.Value);
        activity.Activity.SetTag("kora.source.turn.id", source.Turn.Value);
        try
        {
            token.ThrowIfCancellationRequested();
            await validateSource(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Current() || RequireNativeInput(reviewRequest.SessionId) != request)
            { throw new InvalidOperationException("The exact timing source or native confirmation changed."); }
            bool Eligible() => SourceCurrent() && !token.IsCancellationRequested
                && !confirmation.Activity!.IsStopped && confirmation.Outcome == HostOperationOutcome.Unknown;
            var saved = await configuration.SetAsync(proposed, current.Revision, callRevision,
                request.Origin, SecurityAuditInitiator.LocalUser, policy, Eligible, token).ConfigureAwait(false);
            activity.Complete(saved ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return saved;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        finally { Dispose(); }
    }

    private bool SourceCurrent() => !retired && time.GetUtcNow() < expires && eligible()
        && policy.CheckMutation(source.Request.Origin, callRevision, eligible) is null;
    private bool Current() => SourceCurrent() && configuration.Get() == current;

    private static HostRequest RequireNativeInput(HostId<SessionIdentity> session)
    {
        var activity = HostActivity.RequireCurrent();
        if (activity.Activity!.IsStopped || activity.Outcome != HostOperationOutcome.Unknown
            || activity.Request.Origin != RequestOrigin.LocalUi || activity.Request.SessionId != session
            || activity.Request.InvocationId is not null)
        { throw new InvalidOperationException("Capture timing review requires current native original-user input in the exact session."); }
        return activity.Request;
    }

    public void Dispose()
    {
        lock (gate) { retired = true; consumed = true; }
    }
}
