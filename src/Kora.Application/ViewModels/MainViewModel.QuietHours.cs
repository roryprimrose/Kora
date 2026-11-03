using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Configuration;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private QuietHoursConfigurationService? quietHours;
    private QuietHoursReview? quietHoursReview;
    private Func<bool> quietHoursNativeLifetime = static () => false;
    private bool quietHoursActive;
    private string quietHoursDays = "Mon,Tue,Wed,Thu,Fri";
    private string quietHoursStart = "09:00";
    private string quietHoursEnd = "17:00";
    private string quietHoursZone = "UTC";

    public string QuietHoursDays { get => quietHoursDays; set { if (SetProperty(ref quietHoursDays, value)) { CancelQuietHoursReview(); } } }
    public string QuietHoursStart { get => quietHoursStart; set { if (SetProperty(ref quietHoursStart, value)) { CancelQuietHoursReview(); } } }
    public string QuietHoursEnd { get => quietHoursEnd; set { if (SetProperty(ref quietHoursEnd, value)) { CancelQuietHoursReview(); } } }
    public string QuietHoursZone { get => quietHoursZone; set { if (SetProperty(ref quietHoursZone, value)) { CancelQuietHoursReview(); } } }
    public string QuietHoursStatus => quietHours?.Get().Description ?? "Scheduled quiet hours are unavailable; no schedule was changed.";
    public string QuietHoursReviewText => quietHoursReview is { } current ? current.Description : "No quiet-hours change awaiting confirmation.";
    public bool CanChangeQuietHours => quietHours is not null && !disposed && !quietHoursActive
        && IsCallMutationHostEligible && !IsResponseInteractionPending;
    public bool CanChangeQuietHoursNative => CanChangeQuietHours && quietHoursNativeLifetime();
    public bool CanConfirmQuietHoursNative => CanChangeQuietHoursNative && quietHoursReview is { Initiator: SecurityAuditInitiator.LocalUser } review
        && review.Eligible();
    public AsyncCommand InspectQuietHoursCommand { get; private set; } = null!;
    public AsyncCommand ReviewQuietHoursCommand { get; private set; } = null!;
    public AsyncCommand ResetQuietHoursCommand { get; private set; } = null!;
    public AsyncCommand ConfirmQuietHoursCommand { get; private set; } = null!;
    public AsyncCommand CancelQuietHoursCommand { get; private set; } = null!;

    public void BindQuietHoursNativeLifetime(Func<bool> lifetime)
    {
        quietHoursNativeLifetime = lifetime;
        NotifyQuietHours();
    }

    private void InitializeQuietHours(QuietHoursConfigurationService? service, Func<Func<Task>, AsyncCommand> create)
    {
        quietHours = service;
        if (service?.Get().Saved is { IsOff: false } saved)
        {
            quietHoursDays = saved.Days;
            quietHoursStart = saved.Start.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            quietHoursEnd = saved.End.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            quietHoursZone = saved.ZoneId;
        }
        InspectQuietHoursCommand = create(() => ExecuteQuietHoursCommandAsync(new(AppearanceCommandOperation.Get), SecurityAuditInitiator.LocalUser));
        ReviewQuietHoursCommand = create(() => ExecuteQuietHoursCommandAsync(
            QuietHoursCommand.Parse($"set {QuietHoursCommand.OptionId} to weekly {QuietHoursDays} {QuietHoursStart} {QuietHoursEnd} {QuietHoursZone}", AssistantName)
                ?? throw new InvalidOperationException("Use one exact weekly schedule."), SecurityAuditInitiator.LocalUser));
        ResetQuietHoursCommand = create(() => ExecuteQuietHoursCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser));
        ConfirmQuietHoursCommand = create(() => ExecuteQuietHoursCommandAsync(new(AppearanceCommandOperation.Set, ReviewDecision: true), SecurityAuditInitiator.LocalUser));
        CancelQuietHoursCommand = create(() => ExecuteQuietHoursCommandAsync(new(AppearanceCommandOperation.Set, ReviewDecision: false), SecurityAuditInitiator.LocalUser));
    }

    private void CancelQuietHoursReview()
    {
        quietHoursReview = null;
        quietHours?.Cancel();
        NotifyQuietHours();
    }

    private async Task ExecuteQuietHoursCommandAsync(QuietHoursCommand command, SecurityAuditInitiator initiator,
        ManualCallInput? originalInput = null)
    {
        var native = quietHoursNativeLifetime;
        if (!CanChangeQuietHours || initiator == SecurityAuditInitiator.LocalUser && !native())
        {
            Transcript = "Quiet-hours control requires the owning unlocked host, original input and no pending question or approval.";
            return;
        }
        if (command.ReviewDecision == false || command.Operation == AppearanceCommandOperation.Clarify)
        {
            CancelQuietHoursReview();
            Transcript = command.Error ?? "Quiet-hours review cancelled; no preference changed.";
            return;
        }
        var service = quietHours!;
        var origin = OriginalOrigin(initiator);
        var host = CaptureConfigurationControlEligibility(origin);
        var owner = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var name = assistantNameConfiguration.Get().Revision;
        var consent = HasVoiceConsent;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var admittingInput = true;
        bool Eligible() => !disposed && host() && (!admittingInput || input.Eligible()) && CanRevealPrivatePresentation
            && settingsOwnershipRevision() == owner && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && assistantNameConfiguration.Get().Revision == name && HasVoiceConsent == consent
            && Interlocked.Read(ref voiceRecoveryRevision) == recovery
            && (initiator != SecurityAuditInitiator.LocalUser || ReferenceEquals(native, quietHoursNativeLifetime) && native());
        quietHoursActive = true;
        NotifyQuietHours();
        try
        {
            if (command.ReviewDecision == true)
            {
                var candidate = quietHoursReview ?? throw new InvalidOperationException("Review this exact schedule before separate confirmation.");
                quietHoursReview = null;
                if (!await service.ConfirmAsync(candidate, origin, initiator, communicationPolicy, Eligible, CancellationToken.None))
                { throw new InvalidOperationException("Quiet-hours confirmation was denied by current call/channel policy."); }
            }
            else
            {
                CancelQuietHoursReview();
                await service.InspectAsync(origin, Eligible, CancellationToken.None);
                if (command.Operation != AppearanceCommandOperation.Get)
                {
                    QuietHoursSchedule? value = null;
                    if (command.Operation != AppearanceCommandOperation.Reset)
                    {
                        ArgumentNullException.ThrowIfNull(command.Value, nameof(command));
                        value = command.Value;
                    }
                    quietHoursReview = await service.ReviewAsync(value,
                        origin, initiator, CallPolicyRevision, communicationPolicy, Eligible, CancellationToken.None);
                }
            }
            if (!Eligible()) { throw new InvalidOperationException("Quiet-hours host admission changed before presentation."); }
            Transcript = (quietHoursReview?.Description ?? service.Get().Description) + Environment.NewLine + QuietHoursCommand.Syntax;
            admittingInput = false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            CancelQuietHoursReview();
            QuietHoursControlFailed(logger, exception.GetType().Name);
            if (!disposed) { Transcript = "Quiet hours not confirmed. " + service.Get().Description + " " + exception.Message; }
        }
        finally { quietHoursActive = false; NotifyQuietHours(); }
    }

    private void NotifyQuietHours()
    {
        OnPropertyChanged(nameof(QuietHoursStatus));
        OnPropertyChanged(nameof(QuietHoursReviewText));
        OnPropertyChanged(nameof(CanChangeQuietHours));
        OnPropertyChanged(nameof(CanChangeQuietHoursNative));
        OnPropertyChanged(nameof(CanConfirmQuietHoursNative));
    }
}
