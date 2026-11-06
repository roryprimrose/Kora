using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    internal sealed class RetryProviderReview(ModelProviderSelection selection, ProviderProfileTuple? profile,
        long? sessionRevision, Func<bool> current, Func<CancellationToken, Task> validate)
    {
        internal ProviderProfileTuple? Profile { get; } = profile;
        internal ModelProviderSelection Selection => selection;
        internal bool IsCurrent() => current();
        internal Task ValidateAsync(CancellationToken token) => validate(token);
        internal bool Matches(ModelTurn turn) => turn.Provenance.Selection == selection && turn.Profile == Profile
            && turn.SessionPolicyRevision == sessionRevision && IsCurrent();
    }

    internal async Task<RetryProviderReview> ReviewRetryProviderAsync(SessionWorkspaceEntry target,
        ModelProviderSelection? expected, Func<bool> admitted, CancellationToken token)
    {
        var request = HostActivity.RequireCurrent().Request;
        if (request.SessionId != target.Authority.SessionId || request.InvocationId is not null
            || request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("Retry provider review requires exact original-user host context."); }
        var control = access.ControlRevision;
        var mode = (providerModeConfiguration
            ?? throw new InvalidOperationException("Retry provider policy is unavailable.")).WithInitialMode(
                (value, current) => (Value: value, Current: current));
        ModelProviderPolicy? policy;
        lock (policyGate) { policy = policies.GetValueOrDefault(target.Authority.SessionId); }
        var reviewedPolicy = policy ?? new ModelProviderPolicy(target.Authority.SessionId, new(1), mode.Value,
            ModelProviderSelection.OllamaCandidate, ModelProviderSelection.CopilotCandidate);
        bool PolicyCurrent()
        {
            lock (policyGate)
            {
                var actual = policies.GetValueOrDefault(target.Authority.SessionId);
                return actual == reviewedPolicy || policy is null && actual is null;
            }
        }
        bool Live() => Eligible(control) && admitted() && mode.Current()
            && PolicyCurrent();
        ProviderProfileTuple? tuple = null;
        SessionProviderProfile? session = null;
        Func<bool> resolvedCurrent = Live;
        if (providerProfileConfiguration is not null)
        {
            session = await (sessionProfiles
                ?? throw new InvalidOperationException("Retry session provider authority is unavailable."))
                .ReadProviderProfile(target.Authority.SessionId, token).ConfigureAwait(false);
            var resolved = await providerProfileConfiguration.ResolveTurnAsync(Live, session.Desired, token).ConfigureAwait(false);
            expected ??= resolved.Selection?.Provider == ModelProviderIdentity.Ollama ? resolved.Selection : null;
            if (expected is null || session.Held || session.Session != target.Authority || resolved.Selection != expected || !resolved.Current())
            { throw new InvalidOperationException("The original provider no longer matches the current session/account profile. Review current settings; no fallback."); }
            tuple = resolved.Tuple;
            resolvedCurrent = resolved.Current;
        }
        else
        {
            expected ??= reviewedPolicy.Local;
            if (expected.Provider != ModelProviderIdentity.Ollama || expected != reviewedPolicy.Select(ModelTurnChoice.Default))
            { throw new InvalidOperationException("The recorded original provider has no matching current ordinary/account profile. No fallback is permitted."); }
        }
        async Task Validate(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Live() || !resolvedCurrent())
            { throw new InvalidOperationException("Reviewed provider policy or default changed. Initiate a fresh review."); }
            if (session is not null)
            {
                var actual = await sessionProfiles!.ReadProviderProfile(target.Authority.SessionId, cancellation).ConfigureAwait(false);
                var resolved = await providerProfileConfiguration!.ResolveTurnAsync(Live, actual.Desired, cancellation).ConfigureAwait(false);
                if (actual != session || resolved.Selection != expected || resolved.Tuple != tuple || !resolved.Current())
                { throw new InvalidOperationException("Reviewed provider, account, profile or session policy changed. Nothing may be sent."); }
            }
            if (!Live() || !resolvedCurrent())
            { throw new InvalidOperationException("Reviewed provider admission changed during validation."); }
        }
        await Validate(token).ConfigureAwait(false);
        return new(expected, tuple, session?.Revision, () => Live() && resolvedCurrent(), Validate);
    }
}
