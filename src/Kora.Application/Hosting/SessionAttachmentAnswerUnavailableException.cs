using Kora.Core.Context;

namespace Kora.Application.Hosting;

/// <summary>Source-policy refusal before conversation retention or provider dispatch.</summary>
public sealed class SessionAttachmentAnswerUnavailableException(LocalFileModelUseDecision decision)
    : InvalidOperationException("Local attachment model-use policy is unavailable, denied or revoked (" + decision
        + "). Stored-copy consent does not authorize disclosure.")
{
    public LocalFileModelUseDecision Decision { get; } = decision;
}
