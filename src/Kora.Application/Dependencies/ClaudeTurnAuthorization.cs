using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

public sealed class ClaudeTurnAuthorization : HostedTurnAuthorization
{
    internal ClaudeTurnAuthorization(HostedTransmissionReviewService owner, HostActivity issuer,
        ModelContextEnvelope context, ProviderProfileTuple profile) : base(owner, issuer, context, profile) { }
}
