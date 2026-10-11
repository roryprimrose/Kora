using Kora.Core.Authorization;
using Kora.Core.Providers;

namespace Kora.Core.Storage;

/// <summary>Validated passive policy observation; never execution or mutation authority by itself.</summary>
public sealed record SessionProviderProfile(WorkSessionAuthorization Session, long Revision,
    SessionProviderOverrides Desired, bool Held);
