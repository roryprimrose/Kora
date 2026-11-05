using System.Diagnostics;

using Kora.Application.Communication;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

/// <summary>Retained advice bound to a separate native review; the quiet-hours service owns every write.</summary>
public sealed class QuietHoursSuggestionReview : IDisposable
{
    private readonly QuietHoursConfigurationService configuration;
    private readonly CallCommunicationPolicy policy;
    private readonly TimeProvider time;
    private readonly QuietHoursState current;
    private readonly HostRequest reviewRequest;
    private readonly ModelTurnProvenance source;
    private readonly long callRevision;
    private readonly ActivityLink cause;
    private readonly Lock gate = new();
    private Func<bool> eligible;
    private Func<CancellationToken, Task>? validateSource;
    private QuietHoursReview? candidate;
    private bool consumed;
    private volatile bool retired;

    private QuietHoursSuggestionReview(QuietHoursConfigurationService configuration, CallCommunicationPolicy policy,
        HostRequest request, ModelTurnProvenance source, long callRevision,
        Func<bool> eligible, Func<CancellationToken, Task> validateSource)
    {
        this.configuration = configuration;
        this.policy = policy;
        time = configuration.Time;
        current = configuration.Get();
        reviewRequest = request;
        this.source = source;
        this.callRevision = callRevision;
        this.eligible = eligible;
        this.validateSource = validateSource;
        cause = new(HostActivity.RequireCurrent().Activity!.Context);
    }

    public bool Available { get { lock (gate) { return !consumed && Current(); } } }
    internal bool BelongsTo(QuietHoursConfigurationService owner) => ReferenceEquals(configuration, owner);
    public string Description => Available
        ? $"notices.quiet-hours: Current saved: {(current.Saved is { } saved ? saved.ToString() : "default Off (not saved)")}; effective: {current.Effective}. "
            + $"Proposed: {(candidate!.Value is { } value ? value.ToString() : "reset to default Off (not saved)")}. Default: Off (no zone). "
            + "Weekly schedules show all selected start days, start/end local times and the explicit installed zone ID. "
            + "Scope: shared device-local, future already-consented proactive completion speech only. "
            + "Start inclusive, end exclusive; overnight intervals belong to the start day and repeated DST hours remain quiet. "
            + "Visual notices, ordinary requested answers, listening, speech consent and call/privacy protection are unchanged. "
            + "No synthesis, in-flight answer change or suppressed-completion replay. Nothing has changed. "
            + "Confirm separately in this current native surface within 60 seconds, or Cancel."
        : string.Empty;

    public static async Task<QuietHoursSuggestionReview> ReviewAsync(QuietHoursConfigurationService configuration,
        CallCommunicationPolicy policy, ConversationMessage message, Func<bool> eligible,
        Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Validate();
        var request = RequireNativeInput(message.Request.SessionId);
        if (request.RequestId == message.Request.RequestId || message.Outcome != ModelTurnOutcome.Succeeded)
        { throw new InvalidOperationException("Review requires the exact successful retained answer and separate native input."); }
        var value = RegisteredSettingSuggestions.Parse(message.AssistantResponse!.Body) switch
        {
            RegisteredSettingProposal.QuietHours proposal => proposal.Value,
            RegisteredSettingProposal.Reset { Setting: RegisteredSetting.QuietHours } => null,
            _ => throw new InvalidDataException("Review one exact notices.quiet-hours Set or Reset only."),
        };
        var call = policy.Current.Revision;
        bool Admitted() => eligible() && !policy.Current.IsProtected
            && policy.CheckMutation(message.Request.Origin, call, eligible) is null;
        token.ThrowIfCancellationRequested();
        if (!Admitted()) { throw new InvalidOperationException("The exact quiet-hours source or call policy is unavailable."); }
        await validateSource(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!Admitted() || RequireNativeInput(message.Request.SessionId) != request)
        { throw new InvalidOperationException("The original quiet-hours source or native input changed."); }
        await configuration.InspectAsync(request.Origin, Admitted, token).ConfigureAwait(false);
        var owner = new QuietHoursSuggestionReview(configuration, policy, request, message.Execution!, call, eligible, validateSource);
        try
        {
            owner.candidate = await configuration.ReviewAsync(value, request.Origin, SecurityAuditInitiator.LocalUser,
                call, policy, owner.SourceCurrent, token).ConfigureAwait(false);
            await validateSource(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!owner.Current() || RequireNativeInput(message.Request.SessionId) != request)
            { throw new InvalidOperationException("The quiet-hours source, schedule or native review changed."); }
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }

    public async Task<bool> ConfirmAsync(CancellationToken token)
    {
        var request = RequireNativeInput(reviewRequest.SessionId);
        var confirmation = HostActivity.RequireCurrent();
        if (request.RequestId == reviewRequest.RequestId || request.RequestId == source.Request.RequestId)
        { throw new InvalidOperationException("Confirmation requires separate fresh native input."); }
        Func<CancellationToken, Task> validate;
        lock (gate)
        {
            if (!Available) { throw new InvalidOperationException("Quiet-hours review expired or changed. Review the exact answer again."); }
            consumed = true;
            validate = validateSource!;
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        activity.Activity!.AddLink(cause);
        activity.Activity.SetTag("kora.source.request.id", source.Request.RequestId.Value);
        activity.Activity.SetTag("kora.source.turn.id", source.Turn.Value);
        try
        {
            token.ThrowIfCancellationRequested();
            await validate(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Current() || RequireNativeInput(reviewRequest.SessionId) != request)
            { throw new InvalidOperationException("The exact quiet-hours source or native confirmation changed."); }
            bool Eligible() => SourceCurrent() && !token.IsCancellationRequested
                && !confirmation.Activity!.IsStopped && confirmation.Outcome == HostOperationOutcome.Unknown;
            var saved = await configuration.ConfirmAsync(candidate!, request.Origin, SecurityAuditInitiator.LocalUser,
                policy, Eligible, token).ConfigureAwait(false);
            activity.Complete(saved ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return saved;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        finally { Dispose(); }
    }

    private bool SourceCurrent() => !retired && eligible() && !policy.Current.IsProtected
        && (candidate is null || time.GetUtcNow() >= candidate.Created && time.GetUtcNow() < candidate.Expires)
        && policy.CheckMutation(source.Request.Origin, callRevision, eligible) is null;
    private bool Current() => candidate is not null && SourceCurrent() && configuration.Get() == current;

    private static HostRequest RequireNativeInput(HostId<SessionIdentity> session)
    {
        var activity = HostActivity.RequireCurrent();
        if (activity.Activity!.IsStopped || activity.Outcome != HostOperationOutcome.Unknown
            || activity.Request.Origin != RequestOrigin.LocalUi || activity.Request.SessionId != session
            || activity.Request.InvocationId is not null)
        { throw new InvalidOperationException("Quiet-hours review requires live original native user input in the exact session."); }
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
        if (candidate is { } reviewed) { configuration.Cancel(reviewed); }
    }
}
