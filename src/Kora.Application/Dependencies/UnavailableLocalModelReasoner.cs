using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

/// <summary>The legacy request contract cannot carry exact host/session/task admission and is unavailable.</summary>
public sealed class UnavailableLocalModelReasoner : ILocalModelReasoner
{
    public Task<LocalModelResponse> ReasonAsync(string request, LocalModelContext context,
        LocalModelArtifact? artifact, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Model execution requires an exact admitted ModelTurnHost turn. The legacy bootstrap route is unavailable; "
            + "no request was sent and no model action was proposed. Use an admitted original-request workflow.");
    }
}
