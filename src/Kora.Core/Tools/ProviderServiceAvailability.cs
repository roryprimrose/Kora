using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Core.Tools;

public sealed record ProviderServiceAvailability(ProviderService Service, ProviderCatalogueStatus Status,
    ProviderAccountId? Account, HostRevision Revision, DateTimeOffset ValidUntil, int ModelCount);
