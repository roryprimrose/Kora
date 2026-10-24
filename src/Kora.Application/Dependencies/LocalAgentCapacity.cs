using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>Explicit platform qualification, not configuration or serial runtime evidence.</summary>
internal sealed record LocalAgentCapacity(ModelProviderSelection Selection, ProviderProfileTuple? Profile,
    IModelTurnAdapter Adapter, Guid Revision, DateTimeOffset ObservedAt, DateTimeOffset ExpiresAt, int MaximumRuns);
