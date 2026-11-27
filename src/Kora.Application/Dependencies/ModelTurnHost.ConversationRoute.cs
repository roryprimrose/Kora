using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    /// <summary>
    /// Read-only preview of the session policy mode and resolved service. It only selects the request shape;
    /// admission re-resolves the exact profile and policy and fails closed when they no longer agree.
    /// </summary>
    internal async Task<ConversationProviderRoute> ResolveConversationRouteAsync(HostId<SessionIdentity> session,
        CancellationToken token)
    {
        session.Validate();
        if (providerModeConfiguration is null || providerProfileConfiguration is null || sessionProfiles is null)
        { return ConversationProviderRoute.Local; }
        ModelProviderPolicy? policy;
        lock (policyGate) { policy = policies.GetValueOrDefault(session); }
        var mode = policy?.Mode ?? providerModeConfiguration.WithInitialMode((value, _) => value);
        if (mode == ModelProviderMode.LocalOnly) { return ConversationProviderRoute.Local; }
        if (mode == ModelProviderMode.LocalFirst) { return ConversationProviderRoute.LocalFirst; }
        var saved = await sessionProfiles.ReadProviderProfile(session, token).ConfigureAwait(false);
        if (saved.Held || !saved.Session.IsActive || saved.Session.SessionId != session)
        { return ConversationProviderRoute.Local; }
        var control = access.ControlRevision;
        var resolved = await providerProfileConfiguration.ResolveTurnAsync(() => Eligible(control), saved.Desired, token)
            .ConfigureAwait(false);
        return resolved.Tuple?.Service switch
        {
            ProviderService.Copilot => ConversationProviderRoute.Copilot,
            ProviderService.Claude => ConversationProviderRoute.Claude,
            _ => ConversationProviderRoute.Local,
        };
    }
}
