using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.Dependencies;

internal sealed record VerifiedHandoffTarget(ProviderProfileTuple Profile, ModelProviderSelection Selection,
    SessionProviderProfile Session, Func<bool> Current);
