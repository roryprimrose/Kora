using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>Host-issued intent to prepare one hosted turn; never a network transmission approval.</summary>
public sealed class CopilotTurnAuthorization
{
    internal CopilotTurnAuthorization(HostedTransmissionReviewService owner, HostActivity issuer,
        ModelContextEnvelope context, ProviderProfileTuple profile)
    { Owner = owner; Issuer = issuer; Context = context; Profile = profile; }

    internal HostedTransmissionReviewService Owner { get; }
    internal HostActivity Issuer { get; }
    internal ModelContextEnvelope Context { get; }
    internal int Used;
    public ProviderProfileTuple Profile { get; }
}
