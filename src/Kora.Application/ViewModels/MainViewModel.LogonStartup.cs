using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private LogonStartupConfigurationService? logonStartup;
    private LogonStartupReview? logonStartupReview;
    private Func<bool> logonStartupNativeLifetime = static () => false;
    private bool logonStartupActive;
    public AsyncCommand InspectLogonStartupCommand { get; private set; } = null!;
    public AsyncCommand EnableLogonStartupCommand { get; private set; } = null!;
    public AsyncCommand DisableLogonStartupCommand { get; private set; } = null!;
    public AsyncCommand ResetLogonStartupCommand { get; private set; } = null!;
    public AsyncCommand ConfirmLogonStartupCommand { get; private set; } = null!;
    public AsyncCommand CancelLogonStartupCommand { get; private set; } = null!;
    public string LogonStartupStatus => logonStartup?.Get().Description ?? "Start at logon is unavailable. No registration was changed.";
    public string LogonStartupReviewText => logonStartupReview?.Description ?? "No startup change is awaiting confirmation.";
    public bool CanControlLogonStartup => logonStartup is not null && !disposed && !logonStartupActive
        && IsCallMutationHostEligible && !IsResponseInteractionPending;
    public bool CanControlLogonStartupNative => CanControlLogonStartup && logonStartupNativeLifetime();

    public void BindLogonStartupNativeLifetime(Func<bool> lifetime)
    {
        logonStartupNativeLifetime = lifetime;
        CancelLogonStartupReview();
        NotifyLogonStartup();
    }

    private void InitializeLogonStartup(LogonStartupConfigurationService? service, Func<Func<Task>, AsyncCommand> create)
    {
        logonStartup = service;
        InspectLogonStartupCommand = create(() => NativeLogonStartup(LogonStartupOperation.Inspect));
        EnableLogonStartupCommand = create(() => NativeLogonStartup(LogonStartupOperation.Enable));
        DisableLogonStartupCommand = create(() => NativeLogonStartup(LogonStartupOperation.Disable));
        ResetLogonStartupCommand = create(() => NativeLogonStartup(LogonStartupOperation.Reset));
        ConfirmLogonStartupCommand = create(() => NativeLogonStartup(LogonStartupOperation.Confirm));
        CancelLogonStartupCommand = create(() => NativeLogonStartup(LogonStartupOperation.Cancel));
    }

    private Task NativeLogonStartup(LogonStartupOperation operation) =>
        ExecuteLogonStartup(operation, SecurityAuditInitiator.LocalUser, null);

    private void CancelLogonStartupReview()
    {
        logonStartupReview = null;
        logonStartup?.Cancel();
        OnPropertyChanged(nameof(LogonStartupReviewText));
    }

    private async Task ExecuteLogonStartup(LogonStartupOperation operation, SecurityAuditInitiator initiator,
        ManualCallInput? originalInput)
    {
        if (operation == LogonStartupOperation.Cancel)
        {
            CancelLogonStartupReview();
            Transcript = "Startup review cancelled. No new registration or preference write was requested.";
            return;
        }
        var native = logonStartupNativeLifetime;
        if (operation == LogonStartupOperation.Invalid || !CanControlLogonStartup
            || initiator == SecurityAuditInitiator.LocalUser && !native())
        {
            CancelLogonStartupReview();
            Transcript = "Start at logon unavailable or input not accepted. Use the owning unlocked host with no pending decision. " + LogonStartupCommand.Syntax;
            return;
        }
        var service = logonStartup!;
        var origin = OriginalOrigin(initiator);
        var host = CaptureConfigurationControlEligibility(origin);
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var owner = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var name = assistantNameConfiguration.Get().Revision;
        var admittingInput = true;
        bool Eligible() => !disposed && host() && (!admittingInput || input.Eligible()) && CanRevealPrivatePresentation
            && settingsOwnershipRevision() == owner && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && assistantNameConfiguration.Get().Revision == name
            && (initiator != SecurityAuditInitiator.LocalUser || ReferenceEquals(native, logonStartupNativeLifetime) && native());
        logonStartupActive = true;
        NotifyLogonStartup();
        try
        {
            if (operation == LogonStartupOperation.Inspect)
            {
                CancelLogonStartupReview();
                await service.InspectAsync(origin, Eligible, CancellationToken.None);
            }
            else if (operation == LogonStartupOperation.Confirm)
            {
                var candidate = logonStartupReview ?? throw new InvalidOperationException("Review this exact change first; confirmation is separate.");
                logonStartupReview = null;
                var accepted = await service.ConfirmAsync(candidate, origin, initiator, communicationPolicy, Eligible, CancellationToken.None);
                if (!accepted) { throw new InvalidOperationException("The original call/channel policy denied startup confirmation."); }
            }
            else
            {
                CancelLogonStartupReview();
                logonStartupReview = await service.ReviewAsync(operation == LogonStartupOperation.Reset ? null
                    : operation == LogonStartupOperation.Enable, origin, CallPolicyRevision, communicationPolicy, Eligible, CancellationToken.None);
            }
            if (!Eligible()) { throw new InvalidOperationException("Startup host admission changed before presentation."); }
            Transcript = logonStartupReview?.Description ?? service.Get().Description;
            // A separate voice confirmation has its own freshly admitted capture generation.
            admittingInput = false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            CancelLogonStartupReview();
            ApplicationLog.Error(logger, exception, "Controlling reviewed Windows logon startup");
            if (!disposed)
            { Transcript = "Start at logon not confirmed. " + exception.Message + " Inspect registration, saved intent and audit before manual recovery; no automatic retry."; }
        }
        finally
        {
            logonStartupActive = false;
            NotifyLogonStartup();
        }
    }

    private void NotifyLogonStartup()
    {
        OnPropertyChanged(nameof(LogonStartupStatus));
        OnPropertyChanged(nameof(LogonStartupReviewText));
        OnPropertyChanged(nameof(CanControlLogonStartup));
        OnPropertyChanged(nameof(CanControlLogonStartupNative));
    }
}
