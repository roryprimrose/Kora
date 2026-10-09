using Kora.Core.Hosting;

namespace Kora.Core.Dependencies;

public sealed record ModelTurnProvenance(
    HostId<ModelTurnIdentity> Turn, HostRequest Request, ModelProviderSelection Selection);
