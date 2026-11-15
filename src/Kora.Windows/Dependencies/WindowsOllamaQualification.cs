using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>
/// A trusted host issuer's exact-profile process-use receipt, with release qualification or explicitly separate evaluation admission.
/// Never construct this from preferences, discovery, model text or provider responses.
/// </summary>
public sealed class WindowsOllamaQualification
{
    public WindowsOllamaQualification(Guid evidenceId, ProviderCapabilityCatalogue catalogue,
        WindowsOllamaRuntimeIdentity runtime, string runtimeVersion, string model, string modelDigest,
        CancellationToken revoked, LocalModelQualificationEvidence? qualificationEvidence = null)
        : this(evidenceId, catalogue, runtime, runtimeVersion, model, modelDigest, qualificationEvidence, null, revoked)
    { }

    private WindowsOllamaQualification(Guid evidenceId, ProviderCapabilityCatalogue catalogue,
        WindowsOllamaRuntimeIdentity runtime, string runtimeVersion, string model, string modelDigest,
        LocalModelQualificationEvidence? qualificationEvidence, Func<bool>? evaluationEligibility, CancellationToken revoked)
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
        QualificationEvidence = qualificationEvidence;
        this.evaluationEligibility = evaluationEligibility;
        if (qualificationEvidence is not null && (!string.Equals(qualificationEvidence.ExecutableSha256, runtime.ExecutableSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(qualificationEvidence.RuntimeVersion, runtimeVersion, StringComparison.Ordinal)
            || !string.Equals(qualificationEvidence.Model, model, StringComparison.Ordinal)
            || !string.Equals(qualificationEvidence.ModelSha256, modelDigest, StringComparison.OrdinalIgnoreCase)
            || qualificationEvidence.ProfileId != selected.Selection.Model.Value || qualificationEvidence.Revision != catalogue.Revision.Value
            || qualificationEvidence.IssuedAt > catalogue.ObservedAt || qualificationEvidence.ExpiresAt != catalogue.ValidUntil))
        { throw new ArgumentException("The receipt subject does not match independently verified qualification evidence.", nameof(qualificationEvidence)); }
    }

    private readonly Func<bool>? evaluationEligibility;

    internal static WindowsOllamaQualification ForEvaluation(Guid admissionId, ProviderCapabilityCatalogue catalogue,
        WindowsOllamaRuntimeIdentity runtime, Func<bool> remainsEligible, CancellationToken revoked) =>
        new(admissionId, catalogue, runtime, WindowsOllamaSetupService.PackageVersion, WindowsOllamaSetupService.Model,
            WindowsOllamaSetupService.ModelDigest[7..], null, remainsEligible, revoked);

    public Guid EvidenceId { get; }
    public ProviderCapabilityCatalogue Catalogue { get; }
    public WindowsOllamaRuntimeIdentity Runtime { get; }
    public string RuntimeVersion { get; }
    public string Model { get; }
    public string ModelDigest { get; }
    public CancellationToken Revoked { get; }
    public LocalModelQualificationEvidence? QualificationEvidence { get; }
    public bool IsEvaluation => evaluationEligibility is not null;
    public bool IsCurrent(DateTimeOffset now) => !Revoked.IsCancellationRequested
        && Runtime.StartedAt <= now && Catalogue.IsCurrent(now)
        && (evaluationEligibility is not null ? evaluationEligibility() : QualificationEvidence?.IsCurrent(now) == true);
}