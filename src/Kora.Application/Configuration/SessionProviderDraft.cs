using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.Configuration;

public sealed class SessionProviderDraft
{
    internal SessionProviderDraft(SessionProviderProfile saved, ProviderProfilePreferences app, ProviderCatalogueObservation observation,
        ProviderProfileTuple? effective, RequestOrigin origin, Func<bool> remainsAdmitted, long appRevision)
    {
        Saved = saved;
        App = app;
        Observation = observation;
        Effective = effective;
        Origin = origin;
        RemainsAdmitted = remainsAdmitted;
        AppRevision = appRevision;
    }
    public SessionProviderProfile Saved { get; }
    public ProviderProfilePreferences App { get; }
    public ProviderCatalogueObservation Observation { get; }
    public ProviderProfileTuple? Effective { get; }
    public ProviderService EffectiveService => Saved.Desired.EffectiveService(App);
    public ProviderCapabilityCatalogue? Catalogue => EffectiveService == ProviderService.Unset ? null : Observation.For(EffectiveService);
    public string Status => Saved.Held ? "Unavailable: policy outcome held. Inspect durable audit/control receipts; no automatic retry or reset."
        : Effective is null ? "Unavailable: desired sparse choices retained; no complete current account-bound tuple. Authentication and providers are not composed."
        : "Exact catalogue tuple resolved only; qualification, authentication, egress and context envelopes remain independent closed gates.";
    internal RequestOrigin Origin { get; }
    internal Func<bool> RemainsAdmitted { get; }
    internal long AppRevision { get; }
}
