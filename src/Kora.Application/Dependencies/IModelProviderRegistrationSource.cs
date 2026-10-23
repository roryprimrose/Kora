using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>Host-owned exact-profile qualification resolution, independent of user selection and provider metadata.</summary>
internal interface IModelProviderRegistrationSource
{
    Task<ModelProviderRegistration?> ResolveAsync(ModelProviderSelection selection, ProviderProfileTuple profile,
        CancellationToken cancellationToken);
}
