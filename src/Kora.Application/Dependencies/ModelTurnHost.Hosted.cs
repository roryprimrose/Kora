using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    internal static bool HostedAuthorized(ModelProviderRegistration registration, HostedTurnAuthorization? authorization,
        ModelContextEnvelope context, HostActivity issuer) =>
        authorization is not null && Volatile.Read(ref authorization.Used) == 1
        && ReferenceEquals(authorization.Context, context)
        && ReferenceEquals(authorization.Issuer.Request, issuer.Request)
        && ProviderProfileRules.ServiceFor(registration.Selection.Provider) == authorization.Profile.Service
        && (registration.Adapter, authorization) switch
        {
            (CopilotModelTurnAdapter copilot, CopilotTurnAuthorization issued) => copilot.Owns(issued),
            (ClaudeModelTurnAdapter claude, ClaudeTurnAuthorization issued) => claude.Owns(issued),
            _ => false,
        };

    internal static IModelTurnAdapter BindHosted(IModelTurnAdapter adapter, HostedTurnAuthorization authorization,
        Func<CancellationToken, ValueTask<bool>> current) => (adapter, authorization) switch
        {
            (CopilotModelTurnAdapter copilot, CopilotTurnAuthorization issued) => copilot.Bind(issued, current),
            (ClaudeModelTurnAdapter claude, ClaudeTurnAuthorization issued) => claude.Bind(issued, current),
            _ => throw new InvalidOperationException("Hosted adapter does not own this service authorization."),
        };
}
