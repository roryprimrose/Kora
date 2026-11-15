using Avalonia.Controls;
using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class SettingsView
{
    private WindowsOllamaEvaluationSource? localEvaluation;
    private volatile WindowsOllamaEvaluationReview? localEvaluationReview;
    private volatile WindowsOllamaQualification? localEvaluationReceipt;
    private CancellationTokenSource? localEvaluationCancellation;
    private volatile bool localEvaluationBusy;
    private NativeViewLifetime? localEvaluationPresentation;
    private volatile bool localEvaluationConsented;
    private long localEvaluationConsentRevision;

    internal void BindLocalEvaluation(WindowsOllamaEvaluationSource source, AudioControlAdmission admission, ILogger logger)
    {
        localEvaluation = source;
        var panel = this.FindControl<StackPanel>("LocalEvaluationControls")!;
        panel.IsEnabled = true;
        localEvaluationPresentation?.Dispose();
        var presentation = localEvaluationPresentation = new(panel, owner, () => model.CanRevealPrivatePresentation);
        presentation.PresentationChanged += (_, _) =>
        {
            if (!presentation.IsPresented) { RetireLocalEvaluationReview(); }
        };
        var status = this.FindControl<TextBlock>("LocalEvaluationStatus")!;
        status.Text = "No Ollama evaluation admitted. Inspect, review and explicitly confirm before selecting the evaluation profile.";
        var consent = this.FindControl<CheckBox>("LocalEvaluationConsent")!;
        consent.IsCheckedChanged += (_, _) =>
        {
            localEvaluationConsented = consent.IsChecked == true;
            Interlocked.Increment(ref localEvaluationConsentRevision);
        };
        var inspect = this.FindControl<Button>("ReviewLocalEvaluation")!;
        var approve = this.FindControl<Button>("ConfirmLocalEvaluation")!;
        var revoke = this.FindControl<Button>("RevokeLocalEvaluation")!;
        model.BindOllamaEvaluationQuiescence(() => !localEvaluationBusy && source.IsQuiescent);

        inspect.Command = new AsyncCommand(() => RunLocalEvaluation(async (live, token) =>
        {
            localEvaluationReview = null;
            consent.IsChecked = false;
            var reviewed = await admission.RunObservedAsync(RequestOrigin.LocalUi, live,
                ct => source.ObserveAsync(live, ct), (_, _, result) => result, token);
            if (!live()) { source.DiscardPendingReview(reviewed); return; }
            localEvaluationReview = reviewed;
            status.Text = "Review before allowing inference: " + reviewed.Subject.Runtime.ExecutablePath
                + Environment.NewLine + "Runtime " + WindowsOllamaSetupService.PackageVersion
                + "; model " + WindowsOllamaSetupService.Model
                + Environment.NewLine + "Executable SHA-256: " + reviewed.Subject.Runtime.ExecutableSha256
                + Environment.NewLine + "Model SHA-256: " + WindowsOllamaSetupService.ModelDigest
                + Environment.NewLine + "These bytes are observed, not release-qualified. Review expires in five minutes; confirmation rechecks identity and metadata.";
        }), Failed);

        approve.Command = new AsyncCommand(() => RunLocalEvaluation(async (live, token) =>
        {
            var reviewed = localEvaluationReview;
            if (reviewed is null || !localEvaluationConsented)
            { throw new InvalidOperationException("Review the exact runtime and explicitly accept live evaluation before confirming."); }
            var remainsEligible = model.CaptureHandoffPresentationEligibility();
            var consentRevision = Interlocked.Read(ref localEvaluationConsentRevision);
            bool Confirming() => live() && localEvaluationConsented
                && consentRevision == Interlocked.Read(ref localEvaluationConsentRevision)
                && ReferenceEquals(localEvaluationReview, reviewed);
            await admission.RunObservedAsync(RequestOrigin.LocalUi, Confirming,
                ct => source.RevalidateAsync(reviewed, Confirming, ct),
                (_, _, validated) =>
                {
                    localEvaluationReceipt = source.Admit(validated, Confirming, remainsEligible, token);
                    return true;
                }, token);
            localEvaluationReview = null;
            consent.IsChecked = false;
            if (live())
            {
                status.Text = "Live evaluation admitted for one hour in memory only; not production-qualified. "
                    + "Refresh services/models, select the labelled evaluation model, reasoning Off and Host-bounded context, then save. "
                    + "Authoring still requires review, explicit adoption and separately confirmed Save. No inference ran during setup.";
            }
        }), Failed);

        revoke.Command = new AsyncCommand(() => RunLocalEvaluation(async (live, token) =>
        {
            await admission.RunAsync(RequestOrigin.LocalUi, live, (_, _) =>
            {
                source.RevokeNative(live);
                return true;
            }, token);
            localEvaluationReview = null;
            localEvaluationReceipt = null;
            consent.IsChecked = false;
            if (live()) { status.Text = "Evaluation revoked. The saved profile is not authority; review and confirm again to use the runtime."; }
        }), Failed);

        void Failed(Exception exception)
        {
            RetireLocalEvaluationReview();
            Kora.Application.Diagnostics.ApplicationLog.Error(logger, exception, "Reviewing local model evaluation");
            if (IsNativePresented)
            { status.Text = "Evaluation unavailable: " + exception.Message + " No substitute or automatic retry is authorized."; }
        }
    }

    private async Task RunLocalEvaluation(Func<Func<bool>, CancellationToken, Task> operation)
    {
        if (localEvaluationBusy) { throw new InvalidOperationException("Local evaluation setup is still releasing its owned resources."); }
        var presentation = CapturePresentation();
        var evaluationPresentation = localEvaluationPresentation?.Capture()
            ?? throw new InvalidOperationException("The native evaluation controls are unavailable.");
        bool Live() => presentation() && evaluationPresentation() && model.CanChangeProviderProfile;
        if (!Live()) { throw new InvalidOperationException("Evaluation requires the original visible, unlocked native provider controls."); }
        localEvaluationBusy = true;
        using var cancellation = new CancellationTokenSource();
        localEvaluationCancellation = cancellation;
        try { await operation(Live, cancellation.Token); }
        catch
        {
            if (localEvaluationReceipt is { } owned) { localEvaluation?.Revoke(owned); }
            RetireLocalEvaluationReview();
            throw;
        }
        finally
        {
            localEvaluationCancellation = null;
            localEvaluationBusy = false;
        }
    }

    private void RetireLocalEvaluationReview()
    {
        var reviewed = localEvaluationReview;
        localEvaluationReview = null;
        try { localEvaluationCancellation?.Cancel(); }
        catch (Exception exception)
        {
            model.ReportHostInteractionFailure("Evaluation setup cancellation failed; owned checks must still join. Failure type: "
                + exception.GetType().Name);
        }
        finally { localEvaluation?.DiscardPendingReview(reviewed); }
        var consent = this.FindControl<CheckBox>("LocalEvaluationConsent");
        if (consent is not null) { consent.IsChecked = false; }
    }
}
