namespace Kora.Core.Providers;

public sealed class ProviderCatalogueObservation
{
    public ProviderCatalogueObservation(LocalProviderInstallation installation, IReadOnlyList<ProviderCapabilityCatalogue> catalogues)
    {
        if (!Enum.IsDefined(installation) || catalogues.Count != 4
            || catalogues.Select(item => item.Service).Distinct().Count() != 4)
        { throw new InvalidDataException("Provider observation must describe each service exactly once."); }
        Installation = installation;
        Catalogues = Array.AsReadOnly(catalogues.ToArray());
    }

    public LocalProviderInstallation Installation { get; }
    public IReadOnlyList<ProviderCapabilityCatalogue> Catalogues { get; }
    public ProviderCapabilityCatalogue For(ProviderService service) => Catalogues.Single(item => item.Service == service);
}
