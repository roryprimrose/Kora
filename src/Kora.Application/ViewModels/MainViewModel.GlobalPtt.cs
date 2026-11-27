using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private GlobalPttConfigurationService? globalPtt;
    private Func<bool> globalPttNativeLifetime = static () => false;
    private Func<GlobalPttShortcut?, Task>? applyGlobalPtt;
    private bool globalPttSettingActive;
    private GlobalPttShortcut selectedGlobalPttShortcut = GlobalPttShortcut.Unassigned;
    private string globalPttRegistrationStatus = "Global shortcut is not registered.";

    public IReadOnlyList<GlobalPttShortcut> GlobalPttChoices => GlobalPttShortcut.Choices;
    public GlobalPttShortcut SelectedGlobalPttShortcut
    {
        get => selectedGlobalPttShortcut;
        set => SetProperty(ref selectedGlobalPttShortcut, value);
    }
    public GlobalPttShortcut? EffectiveGlobalPttShortcut => globalPtt?.Get()?.Shortcut;
    public string GlobalPttStatus => (EffectiveGlobalPttShortcut is { } shortcut
        ? "Global hold-to-talk shortcut: " + shortcut.Label + ". "
        : "Global shortcut preference unavailable. Inspect saved state and audit before recovery. ")
        + globalPttRegistrationStatus + " Saving never enables listening or changes consent.";
    public bool CanChangeGlobalPtt => globalPtt?.Get() is not null && !globalPttSettingActive
        && globalPttNativeLifetime() && IsCallMutationHostEligible && !IsResponseInteractionPending;
    public bool IsGlobalPttHostEligible => !disposed && IsHostInputEligible && voiceOwnershipEligible()
        && !IsResponseInteractionPending && HasVoiceConsent;
    public AsyncCommand SaveGlobalPttCommand { get; private set; } = null!;
    public AsyncCommand CancelGlobalPttCommand { get; private set; } = null!;

    public void BindGlobalPtt(GlobalPttConfigurationService service, Func<GlobalPttShortcut?, Task> apply)
    {
        if (globalPtt is not null) { throw new InvalidOperationException("The global PTT configuration already has an owner."); }
        globalPtt = service;
        applyGlobalPtt = apply;
        try { service.Observe(); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        { ApplicationLog.Error(logger, exception, "Reading global PTT preference"); }
        selectedGlobalPttShortcut = EffectiveGlobalPttShortcut ?? GlobalPttShortcut.Unassigned;
        SaveGlobalPttCommand = new(() => ExecuteGlobalPttCommandAsync(
            new(AppearanceCommandOperation.Set, GlobalShortcut: SelectedGlobalPttShortcut, IsGlobal: true),
            SecurityAuditInitiator.LocalUser), exception => ReportHostInteractionFailure(exception.Message));
        CancelGlobalPttCommand = new(() =>
        {
            SelectedGlobalPttShortcut = EffectiveGlobalPttShortcut ?? GlobalPttShortcut.Unassigned;
            return Task.CompletedTask;
        }, exception => ReportHostInteractionFailure(exception.Message));
        RefreshGlobalPtt();
    }

    public void BindGlobalPttNativeLifetime(Func<bool> lifetime)
    {
        globalPttNativeLifetime = lifetime;
        OnPropertyChanged(nameof(CanChangeGlobalPtt));
    }

    public void ReportGlobalPttRegistration(string status)
    {
        globalPttRegistrationStatus = status;
        OnPropertyChanged(nameof(GlobalPttStatus));
    }

    internal async Task ExecuteGlobalPttCommandAsync(ForegroundPttCommand command, SecurityAuditInitiator initiator)
    {
        var service = globalPtt;
        var apply = applyGlobalPtt;
        var native = globalPttNativeLifetime;
        if (service is null || apply is null || globalPttSettingActive || !IsCallMutationHostEligible
            || IsResponseInteractionPending || initiator == SecurityAuditInitiator.LocalUser && !native())
        {
            Transcript = "Global shortcut settings require the owning unlocked host and no pending question or approval.";
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify) { Transcript = command.Error!; return; }
        var origin = OriginalOrigin(initiator);
        var admitted = CaptureAudioControlEligibility(origin);
        bool Eligible() => !disposed && admitted()
            && (initiator != SecurityAuditInitiator.LocalUser || ReferenceEquals(native, globalPttNativeLifetime) && native());
        globalPttSettingActive = true;
        RefreshGlobalPtt();
        try
        {
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                // Retire the old registration before a preference can become unconfirmed.
                await apply(null);
                var preference = service.Get() ?? throw new InvalidOperationException("Global shortcut preference is unconfirmed.");
                if (!await service.SetAsync(command.GlobalShortcut!, preference.Revision, CallPolicyRevision, origin,
                    initiator, communicationPolicy, Eligible, CancellationToken.None))
                { Transcript = "Global shortcut setting denied by current call/channel policy."; return; }
                SelectedGlobalPttShortcut = command.GlobalShortcut!;
                if (!Eligible()) { throw new InvalidOperationException("Global shortcut setting lost original admission."); }
                await apply(EffectiveGlobalPttShortcut);
            }
            if (!Eligible()) { throw new InvalidOperationException("Global shortcut setting lost original admission."); }
            Transcript = GlobalPttStatus;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        {
            ApplicationLog.Error(logger, exception, "Inspecting or changing global PTT preference");
            Transcript = "Global shortcut not available. Inspect saved state, registration and audit before recovery. " + exception.Message;
        }
        finally { globalPttSettingActive = false; RefreshGlobalPtt(); }
    }

    private void RefreshGlobalPtt()
    {
        OnPropertyChanged(nameof(EffectiveGlobalPttShortcut));
        OnPropertyChanged(nameof(GlobalPttStatus));
        OnPropertyChanged(nameof(CanChangeGlobalPtt));
    }
}
