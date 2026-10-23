using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SpeechComponentSetupService? speechComponentSetup;
    private Func<bool> speechComponentNativeLifetime = static () => false;

    public IReadOnlyList<SpeechComponentDefinition> SpeechComponentOffers => speechComponentSetup?.Offers ?? [];
    public IReadOnlyList<SpeechComponentDefinition> SpeechComponentInspectionTargets => speechComponentSetup?.Components ?? [];
    public bool IsSpeechComponentSetupAvailable => speechComponentSetup is not null;

    public void BindSpeechComponentNativeLifetime(Func<bool> lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        speechComponentNativeLifetime = lifetime;
    }

    public Task<SpeechComponentReadiness> InspectSpeechComponentAsync(SpeechComponentIdentity identity,
        CancellationToken cancellationToken) => RequireSpeechComponentSetup().InspectAsync(identity, cancellationToken);

    public Task<SpeechComponentSetupPlan> ReviewSpeechComponentAsync(SpeechComponentIdentity identity,
        SpeechComponentOperation operation, CancellationToken cancellationToken)
    {
        var setup = RequireSpeechComponentSetup();
        var nativeLifetime = speechComponentNativeLifetime;
        var origin = OriginalOrigin();
        var hostEligible = CaptureConfigurationControlEligibility(origin);
        bool Eligible() => hostEligible() && !IsResponseInteractionPending
            && ReferenceEquals(nativeLifetime, speechComponentNativeLifetime) && nativeLifetime();
        return setup.ReviewAsync(identity, operation, Eligible, cancellationToken);
    }

    public Task<SpeechComponentReadiness> ApplySpeechComponentAsync(SpeechComponentSetupPlan plan, bool consent,
        IProgress<string> progress, CancellationToken cancellationToken) =>
        RequireSpeechComponentSetup().ApplyAsync(plan, consent, progress, cancellationToken);

    private SpeechComponentSetupService RequireSpeechComponentSetup()
    {
        if (speechComponentSetup is null || !IsCallMutationHostEligible || IsResponseInteractionPending
            || !speechComponentNativeLifetime() || HostActivityOriginIsNotLocal())
        {
            throw new InvalidOperationException("Optional speech setup requires the current unlocked owning Settings host with no pending review.");
        }
        return speechComponentSetup;
    }

    private static bool HostActivityOriginIsNotLocal() =>
        Kora.Core.Diagnostics.HostActivity.Current?.Request.Origin != RequestOrigin.LocalUi;
}
