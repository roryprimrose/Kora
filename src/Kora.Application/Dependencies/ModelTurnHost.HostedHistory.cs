using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    internal async Task<ClaudeHistoryInputBudget> PrepareClaudeHistoryInputBudgetAsync(
        ModelContextEnvelope context, CancellationToken token)
    {
        var issuer = HostActivity.RequireCurrent();
        var control = access.ControlRevision;
        bool Live() => Eligible(control) && !issuer.Activity!.IsStopped
            && issuer.Outcome == HostOperationOutcome.Unknown;
        if (providerProfileConfiguration is null || sessionProfiles is null
            || !ReferenceEquals(context.Request, issuer.Request) || !HostedConversationHistoryContext.Supports(context) || !Live())
        { throw new InvalidOperationException("Exact Claude history preparation requires current original-session profile authority."); }
        var session = await sessionProfiles.ReadProviderProfile(context.Request.SessionId, token).ConfigureAwait(false);
        if (session.Held || !session.Session.IsActive || session.Session.SessionId != context.Request.SessionId || !Live())
        { throw new InvalidOperationException("The Claude history preparation session is unavailable."); }
        var resolved = await providerProfileConfiguration.ResolveTurnAsync(Live, session.Desired, token).ConfigureAwait(false);
        if (resolved.Tuple is not { Service: ProviderService.Claude } profile
            || resolved.Selection is not { } selection || !resolved.Current())
        { throw new InvalidOperationException("The exact Claude history preparation profile is unavailable."); }
        var registration = registrationSource is null
            ? registrations.SingleOrDefault(item => item.Selection == selection)
            : await registrationSource.ResolveAsync(selection, profile, token).ConfigureAwait(false);
        if (registration?.Adapter is not ClaudeModelTurnAdapter adapter || registration.Selection != selection
            || !registration.IsQualified(time.GetUtcNow()) || !resolved.Current() || !Live())
        { throw new InvalidOperationException("The qualified Claude history preparation adapter is unavailable."); }
        async ValueTask<bool> Current(CancellationToken cancellation) =>
            Live() && resolved.Current()
            && await sessionProfiles.ReadProviderProfile(context.Request.SessionId, cancellation).ConfigureAwait(false) == session
            && Live() && resolved.Current();
        async Task<int> Measure(ModelContextEnvelope candidate, CancellationToken cancellation)
        {
            if (!await Current(cancellation).ConfigureAwait(false) || candidate.Request != context.Request)
            { throw new InvalidOperationException("The exact Claude history preparation profile changed."); }
            var bytes = await adapter.MeasureHistoryInputAsync(selection, profile, candidate, cancellation).ConfigureAwait(false);
            if (bytes <= 0 || !await Current(cancellation).ConfigureAwait(false))
            { throw new InvalidOperationException("Claude history input preparation is invalid or stale."); }
            return bytes;
        }
        return new(Measure, Current);
    }
}
