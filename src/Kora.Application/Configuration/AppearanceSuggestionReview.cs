using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

/// <summary>Inert answer data reviewed by the original local user; never model write authority.</summary>
public sealed class AppearanceSuggestionReview : IDisposable
{
    public static string ModelPolicy =>
        "For a requested appearance suggestion only, the entire final answer may be one inert suggestion: "
        + "'set appearance.theme to system', 'set appearance.theme to light', 'set appearance.theme to dark', "
        + "or 'set appearance.reader-text-scale to <whole percent>' within "
        + $"{ReaderTextScaleSettings.MinimumPercent}-{ReaderTextScaleSettings.MaximumPercent}. "
        + "Do not claim it was applied. This is data only: the original user must separately open native review "
        + "and confirm exact current/proposed values. This appearance operation admits no other setting or write.";

    private readonly AppearanceConfigurationService configuration;
    private readonly HostRequest reviewRequest;
    private readonly Lock gate = new();
    private Func<bool> eligible;
    private Func<CancellationToken, Task>? validateSource;
    private AppearanceOptionState? current;
    private AppearanceValue? proposed;
    private bool consumed;
    private volatile bool retired;

    private AppearanceSuggestionReview(AppearanceConfigurationService configuration, AppearanceOptionState current,
        AppearanceValue proposed, HostRequest reviewRequest, Func<bool> eligible, Func<CancellationToken, Task> validateSource)
    {
        this.configuration = configuration;
        this.current = current;
        this.proposed = proposed;
        this.reviewRequest = reviewRequest;
        this.eligible = eligible;
        this.validateSource = validateSource;
    }

    public bool Available { get { lock (gate) { return !consumed && Current(); } } }
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
                    : string.Empty;
            }
        }
    }

    public static async Task<AppearanceSuggestionReview> ReviewAsync(AppearanceConfigurationService configuration,
        ConversationMessage message, Func<bool> eligible, Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(eligible);
        ArgumentNullException.ThrowIfNull(validateSource);
        message.Validate();
        var request = RequireLocalRequest(message.Request.SessionId);
        // A UI click cannot relabel an original protected/voice request as a native local request.
        if (request.RequestId == message.Request.RequestId || message.Request.Origin != RequestOrigin.LocalUi
            || message.Outcome != ModelTurnOutcome.Succeeded
            || message.AssistantResponse is not { } answer)
        { throw new InvalidOperationException("Appearance review requires the original local UI conversation's admitted successful answer."); }
        var command = AppearanceCommand.Parse(answer.Body, AssistantNameRules.DefaultName);
        if (command is not { Operation: AppearanceCommandOperation.Set, Value: not null, Descriptor: not null }
            || command.Descriptor.Option is not (AppearanceOption.Theme or AppearanceOption.ReaderTextScale))
        { throw new InvalidDataException("No bounded appearance suggestion: request one exact theme or native reader text scale suggestion."); }
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("The original private appearance review source is no longer eligible."); }
        await validateSource(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!eligible() || RequireLocalRequest(message.Request.SessionId) != request)
        { throw new InvalidOperationException("The original appearance review input changed."); }
        var state = configuration.Get(command.Descriptor.Option);
        return new(configuration, state, command.Value, request, eligible, validateSource);
    }

    public async Task<AppearanceApplyResult> ConfirmAsync(CancellationToken token)
    {
        var request = RequireLocalRequest(reviewRequest.SessionId);
        if (request.RequestId == reviewRequest.RequestId)
        { throw new InvalidOperationException("Appearance confirmation requires a separate fresh local user request."); }
        AppearanceOptionState state;
        AppearanceValue value;
        Func<CancellationToken, Task> validate;
        lock (gate)
        {
            if (!Available)
            { throw new InvalidOperationException("Appearance review expired. Review a fresh suggestion; nothing was applied."); }
            consumed = true;
            state = current!;
            value = proposed!;
            validate = validateSource!;
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        try
        {
            token.ThrowIfCancellationRequested();
            await validate(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Current() || RequireLocalRequest(reviewRequest.SessionId) != request)
            { throw new InvalidOperationException("The original appearance source or native confirmation admission changed."); }
            var proposal = configuration.Propose(state.Descriptor.Option, value, state.Revision,
                SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, Current);
            var result = await configuration.ApplyAsync(proposal, token).ConfigureAwait(false);
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

    private static HostRequest RequireLocalRequest(HostId<SessionIdentity> session)
    {
        var activity = HostActivity.RequireCurrent();
        if (activity.Activity!.IsStopped || activity.Outcome != HostOperationOutcome.Unknown
            || activity.Request.Origin != RequestOrigin.LocalUi || activity.Request.SessionId != session
            || activity.Request.InvocationId is not null)
        { throw new InvalidOperationException("Appearance review requires fresh original-user input in the exact local session."); }
        return activity.Request;
    }

    private bool Current() => !retired && eligible();

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
