using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private StartupPresentationConfigurationService? startupPresentation;
    private StartupPresentationReview? startupPresentationReview;
    private Func<bool> startupPresentationNativeLifetime = static () => false;
    private bool startupPresentationActive;
    public AsyncCommand InspectStartupPresentationCommand { get; private set; } = null!;
    public AsyncCommand ReviewTrayStartupCommand { get; private set; } = null!;
    public AsyncCommand ReviewWorkspaceStartupCommand { get; private set; } = null!;
    public AsyncCommand ResetStartupPresentationCommand { get; private set; } = null!;
    public AsyncCommand ConfirmStartupPresentationCommand { get; private set; } = null!;
    public AsyncCommand CancelStartupPresentationCommand { get; private set; } = null!;
    public string StartupPresentationStatus => startupPresentation?.Status ?? "Manual startup presentation is unavailable. No saved intent is assumed.";
    public string StartupPresentationReviewText => startupPresentationReview?.Description ?? "No manual startup presentation change is awaiting confirmation.";
    public bool CanControlStartupPresentation => startupPresentation is not null && !startupPresentationActive
        && IsCallMutationHostEligible && !IsProtectedCall && !IsResponseInteractionPending && startupPresentationNativeLifetime();

    public void BindStartupPresentationNativeLifetime(Func<bool> lifetime)
    {
        startupPresentationNativeLifetime = lifetime;
        CancelStartupPresentationReview();
        NotifyStartupPresentation();
    }

    private void InitializeStartupPresentation(StartupPresentationConfigurationService? service, Func<Func<Task>, AsyncCommand> create)
    {
        startupPresentation = service;
        InspectStartupPresentationCommand = create(() => ControlStartupPresentation(null, inspect: true));
        ReviewTrayStartupCommand = create(() => ControlStartupPresentation(StartupPresentation.Tray));
        ReviewWorkspaceStartupCommand = create(() => ControlStartupPresentation(StartupPresentation.Workspace));
        ResetStartupPresentationCommand = create(() => ControlStartupPresentation(null));
        ConfirmStartupPresentationCommand = create(() => ControlStartupPresentation(null, confirm: true));
        CancelStartupPresentationCommand = create(() => { CancelStartupPresentationReview(); return Task.CompletedTask; });
    }

    private void CancelStartupPresentationReview()
    {
        startupPresentationReview = null;
        startupPresentation?.Cancel();
        OnPropertyChanged(nameof(StartupPresentationReviewText));
    }

    public Func<bool> CaptureStartupPresentationEligibility()
    {
        var initiallyEligible = IsCallMutationHostEligible && !IsProtectedCall && !IsResponseInteractionPending;
        var host = CaptureConfigurationControlEligibility(RequestOrigin.LocalUi);
        var owner = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var input = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var name = assistantNameConfiguration.Get().Revision;
        return () => initiallyEligible && host() && IsCallMutationHostEligible && !IsProtectedCall && !IsResponseInteractionPending
            && CanRevealPrivatePresentation && settingsOwnershipRevision() == owner
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && Interlocked.Read(ref workspaceSurfaceInputRevision) == input
            && assistantNameConfiguration.Get().Revision == name;
    }

    private async Task ControlStartupPresentation(StartupPresentation? value, bool inspect = false, bool confirm = false)
    {
        var native = startupPresentationNativeLifetime;
        if (!CanControlStartupPresentation || OriginalOrigin() != RequestOrigin.LocalUi)
        {
            CancelStartupPresentationReview();
            Transcript = "Manual startup presentation requires this native Settings view on the owning unlocked private host, with no active call or pending decision.";
            return;
        }
        var host = CaptureStartupPresentationEligibility();
        bool Eligible() => host() && ReferenceEquals(native, startupPresentationNativeLifetime) && native();
        startupPresentationActive = true;
        NotifyStartupPresentation();
        try
        {
            if (inspect)
            {
                CancelStartupPresentationReview();
                await startupPresentation!.InspectAsync(Eligible, CancellationToken.None);
            }
            else if (confirm)
            {
                var candidate = startupPresentationReview ?? throw new InvalidOperationException("Review this exact startup presentation change first; confirmation is separate.");
                startupPresentationReview = null;
                await startupPresentation!.ConfirmAsync(candidate, communicationPolicy, Eligible, CancellationToken.None);
            }
            else
            {
                CancelStartupPresentationReview();
                startupPresentationReview = await startupPresentation!.ReviewAsync(value, CallPolicyRevision, communicationPolicy, Eligible, CancellationToken.None);
            }
            if (!Eligible()) { throw new InvalidOperationException("Startup presentation admission changed before native readback."); }
            Transcript = startupPresentationReview?.Description ?? StartupPresentationStatus;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException
            or InvalidDataException or OperationCanceledException)
        {
            CancelStartupPresentationReview();
            ApplicationLog.Error(logger, exception, "Controlling reviewed manual startup presentation");
            if (!disposed) { Transcript = "Manual startup presentation not confirmed. " + exception.Message + " Inspect saved intent and audit before manual recovery."; }
        }
        finally
        {
            startupPresentationActive = false;
            NotifyStartupPresentation();
        }
    }

    private void NotifyStartupPresentation()
    {
        OnPropertyChanged(nameof(StartupPresentationStatus));
        OnPropertyChanged(nameof(StartupPresentationReviewText));
        OnPropertyChanged(nameof(CanControlStartupPresentation));
    }
}
