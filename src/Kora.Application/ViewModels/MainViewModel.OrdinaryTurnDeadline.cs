using System.Globalization;

using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly OrdinaryTurnDeadlineConfigurationService? ordinaryDeadlineConfiguration;
    private OrdinaryTurnDeadlineObservation? ordinaryDeadlineObservation;
    private Func<bool> ordinaryDeadlineNativeLifetime = static () => false;
    private bool ordinaryDeadlineControlActive;
    private int selectedOrdinaryDeadlineMinutes = OrdinaryTurnDeadline.DefaultMinutes;
    private string ordinaryDeadlineStatus = "Refresh to review the future ordinary task deadline.";

    public int SelectedOrdinaryDeadlineMinutes
    {
        get => selectedOrdinaryDeadlineMinutes;
        set
        {
            if (SetProperty(ref selectedOrdinaryDeadlineMinutes, value))
            { OnPropertyChanged(nameof(IsOrdinaryDeadlineDraftChanged)); }
        }
    }
    public int? SavedOrdinaryDeadlineMinutes => ordinaryDeadlineObservation?.Effective.Minutes;
    public bool IsOrdinaryDeadlineDraftChanged => SavedOrdinaryDeadlineMinutes is { } saved && saved != SelectedOrdinaryDeadlineMinutes;
    public string OrdinaryDeadlineStatus { get => ordinaryDeadlineStatus; private set => SetProperty(ref ordinaryDeadlineStatus, value); }
    private bool CanChangeOrdinaryDeadline => ordinaryDeadlineConfiguration is not null && IsCallMutationHostEligible
        && !ordinaryDeadlineControlActive && !IsResponseInteractionPending;
    public bool CanChangeOrdinaryDeadlineNative => CanChangeOrdinaryDeadline && ordinaryDeadlineNativeLifetime();
    public AsyncCommand RefreshOrdinaryDeadlineCommand { get; }
    public AsyncCommand SaveOrdinaryDeadlineCommand { get; }
    public AsyncCommand ResetOrdinaryDeadlineCommand { get; }

    public void BindOrdinaryDeadlineNativeLifetime(Func<bool> lifetime)
    {
        ordinaryDeadlineNativeLifetime = lifetime;
        ordinaryDeadlineObservation = null;
        OnPropertyChanged(nameof(CanChangeOrdinaryDeadlineNative));
        OnPropertyChanged(nameof(SavedOrdinaryDeadlineMinutes));
        OnPropertyChanged(nameof(IsOrdinaryDeadlineDraftChanged));
    }

    internal async Task ExecuteOrdinaryDeadlineCommandAsync(OrdinaryTurnDeadlineCommand command,
        SecurityAuditInitiator initiator, CancellationToken token = default)
    {
        var lifetime = ordinaryDeadlineNativeLifetime;
        if (!CanChangeOrdinaryDeadline || initiator == SecurityAuditInitiator.LocalUser && !lifetime())
        {
            Transcript = "Task deadline control requires the original unlocked host and no pending question or approval.";
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowFailure("Clarify future task deadline.", command.Error!);
            return;
        }
        var origin = OriginalOrigin(initiator);
        var hostEligible = CaptureConfigurationControlEligibility(origin);
        bool Eligible() => hostEligible() && (initiator != SecurityAuditInitiator.LocalUser
            || ReferenceEquals(lifetime, ordinaryDeadlineNativeLifetime) && lifetime());
        ordinaryDeadlineControlActive = true;
        OnPropertyChanged(nameof(CanChangeOrdinaryDeadlineNative));
        try
        {
            var service = ordinaryDeadlineConfiguration!;
            var observation = initiator == SecurityAuditInitiator.LocalUser
                && command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset
                ? ordinaryDeadlineObservation ?? throw new InvalidOperationException("Refresh the deadline before saving.")
                : await service.RefreshAsync(origin, Eligible, token);
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var value = command.Operation == AppearanceCommandOperation.Reset ? (OrdinaryTurnDeadline?)null
                    : OrdinaryTurnDeadline.Parse(command.Value ?? throw new InvalidOperationException("Choose exact integer minutes."));
                if (!await service.SaveAsync(observation, value, origin, initiator, communicationPolicy, CallPolicyRevision, Eligible, token))
                { throw new InvalidOperationException("Deadline change was denied because host or call admission changed."); }
                observation = await service.RefreshAsync(origin, Eligible, token);
            }
            if (!Eligible()) { throw new InvalidOperationException("Deadline readback admission changed."); }
            ordinaryDeadlineObservation = observation;
            SelectedOrdinaryDeadlineMinutes = observation.Effective.Minutes;
            OrdinaryDeadlineStatus = $"Future ordinary task deadline: {observation.Effective.Minutes} minutes "
                + (observation.Saved is null ? "(default). " : "(saved). ")
                + "Running turns are unchanged. Earlier context expiry and fixed provider/tool limits always win. "
                + "Clarification and approval turns retain their existing fixed limits.";
            Transcript = OrdinaryDeadlineStatus;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or ArgumentOutOfRangeException or OperationCanceledException)
        {
            ordinaryDeadlineObservation = null;
            OrdinaryDeadlineStatus = "Future task deadline unavailable: " + exception.Message;
            ApplicationLog.Error(logger, exception, "Configuring future ordinary task deadline");
            ShowFailure("Task deadline not confirmed.", exception.Message);
        }
        finally
        {
            ordinaryDeadlineControlActive = false;
            OnPropertyChanged(nameof(SavedOrdinaryDeadlineMinutes));
            OnPropertyChanged(nameof(IsOrdinaryDeadlineDraftChanged));
            OnPropertyChanged(nameof(CanChangeOrdinaryDeadlineNative));
        }
    }

    private Task SaveOrdinaryDeadlineAsync() => ExecuteOrdinaryDeadlineCommandAsync(
        new(AppearanceCommandOperation.Set, SelectedOrdinaryDeadlineMinutes.ToString(CultureInfo.InvariantCulture)),
        SecurityAuditInitiator.LocalUser);
}
