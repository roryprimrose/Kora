using Kora.Core.Hosting;

namespace Kora.Core.Providers;

public sealed record ProviderProfileTuple(ProviderService Service, ProviderAccountId Account, HostRevision CatalogueRevision,
    ProviderChoiceId Model, ProviderChoiceId Reasoning, ProviderChoiceId Context);
