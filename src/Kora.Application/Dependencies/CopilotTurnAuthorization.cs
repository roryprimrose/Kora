using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>Host-issued intent to prepare one hosted turn; never a network transmission approval.</summary>
public sealed class CopilotTurnAuthorization : HostedTurnAuthorization
{
    internal CopilotTurnAuthorization(HostedTransmissionReviewService owner, HostActivity issuer,
        ModelContextEnvelope context, ProviderProfileTuple profile)
        : base(owner, issuer, context, profile) { }
}
