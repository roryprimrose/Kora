using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>
/// A trusted host issuer's exact-profile qualification and process-use receipt, not installation metadata.
/// Never construct this from preferences, discovery, model text or provider responses.
/// </summary>
public sealed class WindowsOllamaQualification
{
    public WindowsOllamaQualification(Guid evidenceId, ProviderCapabilityCatalogue catalogue,
        WindowsOllamaRuntimeIdentity runtime, string runtimeVersion, string model, string modelDigest,
        CancellationToken revoked)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ProviderProfileRules.ValidateCatalogue(catalogue);
        if (evidenceId == Guid.Empty || catalogue.Service != ProviderService.Local
            || catalogue.Status != ProviderCatalogueStatus.Current || catalogue.Models.Count != 1
            || catalogue.Models[0].Reasoning.Count != 1 || catalogue.Models[0].Context.Count != 1
            || catalogue.Models[0].Selection.Provider != ModelProviderIdentity.Ollama
            || !WindowsOllamaToolEndpointBinding.IsDigest(modelDigest) || !revoked.CanBeCanceled
            || runtime.StartedAt > catalogue.ObservedAt)
        { throw new ArgumentException("A revocable, complete, single exact local-profile receipt is required.", nameof(catalogue)); }
        // The native writer currently implements exactly these controls; evidence cannot advertise others.
        var selected = catalogue.Models[0];
        if (!string.Equals(selected.Reasoning[0].Id.Value, "off", StringComparison.Ordinal) || selected.Reasoning[0].ProviderManaged
            || !string.Equals(selected.Context[0].Id.Value, "bounded", StringComparison.Ordinal) || !selected.Context[0].ProviderManaged)
        { throw new ArgumentException("Only the implemented think=false, host-bounded context profile is supported.", nameof(catalogue)); }
        EvidenceId = evidenceId;
        Catalogue = catalogue;
        Runtime = runtime;
        RuntimeVersion = runtimeVersion;
        Model = model;
        ModelDigest = modelDigest;
        Revoked = revoked;
    }

    public Guid EvidenceId { get; }
    public ProviderCapabilityCatalogue Catalogue { get; }
    public WindowsOllamaRuntimeIdentity Runtime { get; }
    public string RuntimeVersion { get; }
    public string Model { get; }
    public string ModelDigest { get; }
    public CancellationToken Revoked { get; }
    public bool IsCurrent(DateTimeOffset now) => !Revoked.IsCancellationRequested
        && Runtime.StartedAt <= now && Catalogue.IsCurrent(now);
}
