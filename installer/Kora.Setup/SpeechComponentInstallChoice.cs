using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;

namespace Kora.Setup;

public sealed class SpeechComponentInstallChoice
{
    private readonly SpeechComponentSetupService setup;
    private readonly SpeechComponentSetupPlan plan;
    private readonly Func<HostActivity> continuation;

    private SpeechComponentInstallChoice(SpeechComponentSetupService setup, SpeechComponentSetupPlan plan,
        Func<HostActivity> continuation)
    {
        this.setup = setup;
        this.plan = plan;
        this.continuation = continuation;
    }

    public bool Selected { get; set; }
    public SpeechComponentIdentity Identity => plan.Component.Identity;
    public string Name => plan.Component.Name;
    public string Disclosure => plan.Disclosure;
    public SpeechComponentReadiness Readiness => plan.Readiness;

    public static async Task<IReadOnlyList<SpeechComponentInstallChoice>> InspectAsync(
        SpeechComponentSetupService setup, Func<bool> admitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var choices = new List<SpeechComponentInstallChoice>();
        foreach (var component in setup.Offers)
        {
            var reviewed = await setup.ReviewAsync(component.Identity, SpeechComponentOperation.Prepare, admitted,
                cancellationToken);
            choices.Add(new(setup, reviewed,
                HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Storage)));
        }
        return choices.AsReadOnly();
    }

    public async Task PrepareAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!Selected) { throw new InvalidOperationException("An unchecked speech component cannot be prepared."); }
        using var activity = continuation();
        try
        {
            var result = await setup.ApplyAsync(plan, consent: true, progress, cancellationToken);
            if (result.Storage?.Verified != true || result.Runtime.Functional != SpeechComponentFunctionalEvidence.Verified)
            {
                throw new InvalidDataException("Optional speech data did not pass exact verification. Kora remains installed.");
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
    }
}
