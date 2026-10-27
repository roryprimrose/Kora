using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>Host-owned exact runtime/account/profile qualification; never implemented by a model or preference.</summary>
internal interface IRuntimeCapacityQualification
{
    /// <summary>Returns the same current evidence identity until revocation or runtime generation replacement.</summary>
    RuntimeCapacityEvidence? Observe(ModelProviderSelection selection, ProviderProfileTuple profile);
}