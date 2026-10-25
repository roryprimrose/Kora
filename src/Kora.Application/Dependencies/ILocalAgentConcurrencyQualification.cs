using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>Trusted platform seam. Null means concurrency is unavailable, never implicitly qualified.</summary>
internal interface ILocalAgentConcurrencyQualification
{
    LocalAgentCapacity? Observe(ModelProviderSelection selection, ProviderProfileTuple? profile, IModelTurnAdapter adapter);
    bool IsCurrent(LocalAgentCapacity capacity);
}
