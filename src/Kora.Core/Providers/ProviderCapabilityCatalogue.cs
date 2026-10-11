using Kora.Core.Hosting;

namespace Kora.Core.Providers;

public sealed class ProviderCapabilityCatalogue
{
    public ProviderCapabilityCatalogue(ProviderService service, ProviderCatalogueStatus status, ProviderAccountId? account,
        HostRevision revision, DateTimeOffset observedAt, DateTimeOffset validUntil, IReadOnlyList<ProviderCatalogueModel> models)
    {
        Service = service;
        Status = status;
        Account = account;
        Revision = revision;
        ObservedAt = observedAt;
        ValidUntil = validUntil;
        Models = Array.AsReadOnly(models.ToArray());
        ProviderProfileRules.ValidateCatalogue(this);
    }

    public ProviderService Service { get; }
    public ProviderCatalogueStatus Status { get; }
    public ProviderAccountId? Account { get; }
    public HostRevision Revision { get; }
    public DateTimeOffset ObservedAt { get; }
    public DateTimeOffset ValidUntil { get; }
    public IReadOnlyList<ProviderCatalogueModel> Models { get; }
    public bool IsCurrent(DateTimeOffset now) => Status == ProviderCatalogueStatus.Current && ObservedAt <= now && now < ValidUntil;
}
