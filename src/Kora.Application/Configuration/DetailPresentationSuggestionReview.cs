using System.Diagnostics;

using Kora.Application.Communication;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Auditing;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.Configuration;

/// <summary>Binds inert retained advice to the original native Details review and confirmation owner.</summary>
public sealed class DetailPresentationSuggestionReview : IDisposable
{
    private readonly DetailPresentationConfigurationService configuration;
    private readonly CallCommunicationPolicy policy;
    private readonly TimeProvider time;
    private readonly HostRequest reviewRequest;
    private readonly ModelTurnProvenance source;
    private readonly long callRevision;
    private readonly DateTimeOffset created;
    private readonly ActivityLink cause;
    private readonly Lock gate = new();
    private Func<bool> eligible;
    private Func<CancellationToken, Task>? validateSource;
    private DetailPresentationReview? candidate;
    private bool consumed;
    private volatile bool retired;

    private DetailPresentationSuggestionReview(DetailPresentationConfigurationService configuration,
        CallCommunicationPolicy policy, TimeProvider time, HostRequest request, ConversationMessage source,
        Func<bool> eligible, Func<CancellationToken, Task> validateSource)
    {
        this.configuration = configuration;
        this.policy = policy;
        this.time = time;
        reviewRequest = request;
        this.source = source.Execution!;
        this.eligible = eligible;
        this.validateSource = validateSource;
        callRevision = policy.Current.Revision;
        created = time.GetUtcNow();
        cause = new(HostActivity.RequireCurrent().Activity!.Context);
    }

    public bool Available { get { lock (gate) { return !consumed && candidate is not null && Current(); } } }
    internal bool BelongsTo(DetailPresentationConfigurationService owner) => ReferenceEquals(configuration, owner);
    public string Description => Available
        ? candidate!.Description + " Source: exact retained suggestion. Nothing has changed; original native confirmation is separate."
        : string.Empty;

    public static async Task<DetailPresentationSuggestionReview> ReviewAsync(
        DetailPresentationConfigurationService configuration, CallCommunicationPolicy policy, TimeProvider time,
        ConversationMessage message, Func<bool> eligible, Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Validate();
        var request = RequireNativeInput(message.Request.SessionId);
        if (request.RequestId == message.Request.RequestId || message.Outcome != ModelTurnOutcome.Succeeded)
        { throw new InvalidOperationException("Review requires the exact successful retained answer and separate native input."); }
        var value = RegisteredSettingSuggestions.Parse(message.AssistantResponse!.Body) switch
        {
            RegisteredSettingProposal.DetailPresentation proposal => proposal.Value,
            RegisteredSettingProposal.Reset { Setting: RegisteredSetting.DetailPresentation } => DetailPresentationPreference.Default,
            _ => throw new InvalidDataException("Review one exact responses.detail-presentation Set or Reset only."),
        };
        var owner = new DetailPresentationSuggestionReview(configuration, policy, time, request, message, eligible, validateSource);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!owner.Current()) { throw new InvalidOperationException("The exact Details source or native admission is unavailable."); }
            await validateSource(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!owner.Current() || RequireNativeInput(message.Request.SessionId) != request)
            { throw new InvalidOperationException("The original Details source or native input changed."); }
            owner.candidate = await configuration.ReviewAsync(value, request.Origin, owner.callRevision, policy,
                owner.Current, token).ConfigureAwait(false);
            await validateSource(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!owner.Current() || RequireNativeInput(message.Request.SessionId) != request)
            { throw new InvalidOperationException("The Details source or native review changed."); }
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
            if (!Available) { throw new InvalidOperationException("Details review expired or changed. Review the exact answer again."); }
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
            { throw new InvalidOperationException("The exact Details source or native confirmation changed."); }
            bool Eligible() => Current() && !token.IsCancellationRequested
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

    private bool Current() => !retired && eligible() && time.GetUtcNow() >= created
        && (candidate is null || time.GetUtcNow() < candidate.Expires)
        && policy.Current.Revision == callRevision
        && policy.CheckMutation(source.Request.Origin, callRevision, eligible) is null;

    private static HostRequest RequireNativeInput(HostId<SessionIdentity> session)
    {
        var activity = HostActivity.RequireCurrent();
        if (activity.Activity!.IsStopped || activity.Outcome != HostOperationOutcome.Unknown
            || activity.Request.Origin != RequestOrigin.LocalUi || activity.Request.SessionId != session
            || activity.Request.InvocationId is not null)
        { throw new InvalidOperationException("Details review requires live original native user input in the exact session."); }
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
        // Retiring this source must not cancel a newer review owned by native Settings.
    }
}
