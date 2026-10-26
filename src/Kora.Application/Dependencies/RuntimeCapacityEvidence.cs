using System.Collections.Immutable;

using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>
/// Enablement evidence supplied only at the trusted runtime seam. This is not an allocation capability.
/// Production has no two-transport qualification source until exact runtime/resource evidence is available.
/// </summary>
internal sealed record RuntimeCapacityEvidence(
    ModelProviderSelection Selection, ProviderProfileTuple Profile, Guid RuntimeGeneration,
    DateTimeOffset ObservedAt, DateTimeOffset ExpiresAt, ImmutableArray<RuntimeResourceClaim> Resources,
    Func<IModelTurnAdapter, bool> OwnsIndependentTransport, CancellationToken Revoked);