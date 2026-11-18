using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>Host-issued intent to prepare one hosted turn; never a network transmission approval.</summary>
public sealed class CopilotTurnAuthorization : HostedTurnAuthorization
{
    internal CopilotTurnAuthorization(HostedTransmissionReviewService owner, HostActivity issuer,
        ModelContextEnvelope context, ProviderProfileTuple profile,
        CopilotCustomizationSelection? selection = null,
        Func<CancellationToken, ValueTask<bool>>? sourceCurrent = null)
        : base(owner, issuer, context, profile)
    { Selection = selection; SourceCurrent = sourceCurrent; }

    internal CopilotCustomizationSelection? Selection { get; }
    internal Func<CancellationToken, ValueTask<bool>>? SourceCurrent { get; }
}
