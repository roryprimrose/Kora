using Kora.Application.Dependencies;
using Kora.Core.Dependencies;

namespace Kora;

internal static class OllamaModelProviderFactory
{
    internal static ModelProviderRegistration CreateUnavailable(IModelTurnAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        // A native transport is implementation, not exact-profile qualification evidence.
        return new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.None, DateTimeOffset.MinValue, adapter);
    }
}
