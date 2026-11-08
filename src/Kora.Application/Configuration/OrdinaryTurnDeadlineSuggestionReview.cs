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

/// <summary>Inert retained advice; only separate native confirmation reaches the existing deadline writer.</summary>
public sealed class OrdinaryTurnDeadlineSuggestionReview : IDisposable
{
    private readonly OrdinaryTurnDeadlineConfigurationService configuration;
    private readonly CallCommunicationPolicy policy;
    private readonly TimeProvider time;
    private readonly HostRequest reviewRequest;
    private readonly ModelTurnProvenance source;
    private readonly OrdinaryTurnDeadline? proposed;
    private readonly long callRevision;
    private readonly DateTimeOffset created;
    private readonly ActivityLink cause;
    private readonly Lock gate = new();
    private Func<bool> eligible;
    private Func<CancellationToken, Task>? validateSource;
    private OrdinaryTurnDeadlineObservation? observation;
    private bool consumed;
    private volatile bool retired;

    private OrdinaryTurnDeadlineSuggestionReview(OrdinaryTurnDeadlineConfigurationService configuration,
        CallCommunicationPolicy policy, TimeProvider time, HostRequest request, ConversationMessage message,
        OrdinaryTurnDeadline? proposed, Func<bool> eligible, Func<CancellationToken, Task> validateSource)
    {
        this.configuration = configuration;
        this.policy = policy;
        this.time = time;
        reviewRequest = request;
        source = message.Execution!;
        this.proposed = proposed;
        this.eligible = eligible;
        this.validateSource = validateSource;
        callRevision = policy.Current.Revision;
        created = time.GetUtcNow();
        cause = new(HostActivity.RequireCurrent().Activity!.Context);
    }

    public bool Available { get { lock (gate) { return !consumed && observation is not null && Current(); } } }
    internal bool BelongsTo(OrdinaryTurnDeadlineConfigurationService owner) => ReferenceEquals(configuration, owner);
    public string Description => Available
        ? $"work.active-deadline-minutes: Current saved: {(observation!.Saved is { } saved ? $"{saved.Minutes} minutes" : "default five minutes (not saved)")}; "
            + $"effective: {observation.Effective.Minutes} minutes. Proposed: {(proposed is { } value ? $"{value.Minutes} minutes" : "reset to default five minutes (not saved)")}. "
            + "Default: five minutes. Scope: shared device-local, future ordinary task dispatch only. "
            + "Running turns are unchanged. Earlier original context expiry and fixed provider/tool ceilings always win. "
            + "Clarification and approval limits, concurrency, privacy, consent and call protections are unchanged. "
            + "Nothing has changed. Confirm separately in this current native surface within 60 seconds, or Cancel."
        : string.Empty;

    public static async Task<OrdinaryTurnDeadlineSuggestionReview> ReviewAsync(
        OrdinaryTurnDeadlineConfigurationService configuration, CallCommunicationPolicy policy, TimeProvider time,
        ConversationMessage message, Func<bool> eligible, Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Validate();
        var request = RequireNativeInput(message.Request.SessionId);
        if (request.RequestId == message.Request.RequestId || message.Outcome != ModelTurnOutcome.Succeeded
            || message.Request.Origin != RequestOrigin.LocalUi || message.Request.InvocationId is not null)
        { throw new InvalidOperationException("Deadline review requires the exact successful original local user answer and separate native input."); }
        var value = RegisteredSettingSuggestions.Parse(message.AssistantResponse!.Body) switch
        {
            RegisteredSettingProposal.OrdinaryDeadline proposal => proposal.Value,
            RegisteredSettingProposal.Reset { Setting: RegisteredSetting.OrdinaryTurnDeadline } => (OrdinaryTurnDeadline?)null,
            _ => throw new InvalidDataException("Review one exact work.active-deadline-minutes Set or Reset only."),
        };
        var owner = new OrdinaryTurnDeadlineSuggestionReview(configuration, policy, time, request, message, value, eligible, validateSource);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!owner.SourceCurrent()) { throw new InvalidOperationException("The exact deadline source or native admission is unavailable."); }
            await validateSource(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!owner.SourceCurrent() || RequireNativeInput(message.Request.SessionId) != request)
            { throw new InvalidOperationException("The original deadline source or native input changed."); }
            owner.observation = await configuration.RefreshAsync(request.Origin, owner.SourceCurrent, token).ConfigureAwait(false);
            await validateSource(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!owner.Current() || RequireNativeInput(message.Request.SessionId) != request)
            { throw new InvalidOperationException("The deadline preference, exact source or native review changed."); }
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }

    public async Task<bool> ConfirmAsync(CancellationToken token)
    {
        var request = RequireNativeInput(reviewRequest.SessionId);
        var confirmation = HostActivity.RequireCurrent();
        if (request.RequestId == reviewRequest.RequestId || request.RequestId == source.Request.RequestId)
        { throw new InvalidOperationException("Deadline confirmation requires separate fresh native input."); }
        Func<CancellationToken, Task> validate;
        lock (gate)
        {
            if (!Available) { throw new InvalidOperationException("Deadline review expired or changed. Review the exact answer again."); }
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
            { throw new InvalidOperationException("The exact deadline source, preference or native confirmation changed."); }
            bool Eligible() => SourceCurrent() && !token.IsCancellationRequested
                && !confirmation.Activity!.IsStopped && confirmation.Outcome == HostOperationOutcome.Unknown;
            var saved = await configuration.SaveAsync(observation!, proposed, request.Origin, SecurityAuditInitiator.LocalUser,
                policy, callRevision, Eligible, token).ConfigureAwait(false);
            activity.Complete(saved ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return saved;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        finally { Dispose(); }
    }

    private bool SourceCurrent()
    {
        if (retired) { return false; }
        if (eligible() && time.GetUtcNow() >= created && time.GetUtcNow() < created.AddSeconds(60)
            && !policy.Current.IsProtected && policy.Current.Revision == callRevision
            && policy.CheckMutation(source.Request.Origin, callRevision, eligible) is null) { return true; }
        retired = true;
        return false;
    }

    private bool Current() => SourceCurrent() && observation is not null && configuration.IsCurrent(observation);

    private static HostRequest RequireNativeInput(HostId<SessionIdentity> session)
    {
        var activity = HostActivity.RequireCurrent();
        if (activity.Activity!.IsStopped || activity.Outcome != HostOperationOutcome.Unknown
            || activity.Request.Origin != RequestOrigin.LocalUi || activity.Request.SessionId != session
            || activity.Request.InvocationId is not null)
        { throw new InvalidOperationException("Deadline review requires live original native user input in the exact session."); }
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
