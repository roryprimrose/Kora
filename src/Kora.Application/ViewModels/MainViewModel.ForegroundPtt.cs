using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ForegroundPttConfigurationService? foregroundPtt;
    private Func<bool> foregroundPttNativeLifetime = static () => false;
    private bool foregroundPttSettingActive;
    private ForegroundPttShortcut selectedForegroundPttShortcut = ForegroundPttShortcut.Unassigned;
    public IReadOnlyList<ForegroundPttShortcut> ForegroundPttChoices => ForegroundPttShortcut.Choices;
    public ForegroundPttShortcut SelectedForegroundPttShortcut
    {
        get => selectedForegroundPttShortcut;
        set => SetProperty(ref selectedForegroundPttShortcut, value);
    }
    public ForegroundPttShortcut? EffectiveForegroundPttShortcut => foregroundPtt?.Get()?.Shortcut;
    public string ForegroundPttStatus => EffectiveForegroundPttShortcut is { } shortcut
        ? "Foreground Response shortcut: " + shortcut.Label + ". Hold to speak, release to finish. Saving never enables listening or changes consent."
        : "Foreground shortcut unavailable. Inspect saved preference and audit before recovery; the microphone button is unchanged.";
    public bool CanChangeForegroundPtt => foregroundPtt?.Get() is not null && !foregroundPttSettingActive
        && foregroundPttNativeLifetime() && IsCallMutationHostEligible && !IsResponseInteractionPending;
    public AsyncCommand SaveForegroundPttCommand { get; private set; } = null!;
    public AsyncCommand CancelForegroundPttCommand { get; private set; } = null!;

    public void BindForegroundPttNativeLifetime(Func<bool> lifetime)
    {
        foregroundPttNativeLifetime = lifetime;
        OnPropertyChanged(nameof(CanChangeForegroundPtt));
    }

    private void InitializeForegroundPtt(ForegroundPttConfigurationService? service, Func<Func<Task>, AsyncCommand> createCommand)
    {
        foregroundPtt = service;
        if (service is not null)
        {
            try { service.Observe(); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            { ApplicationLog.Error(logger, exception, "Reading foreground PTT preference"); }
            service.Changed += OnForegroundPttChanged;
        }
        selectedForegroundPttShortcut = EffectiveForegroundPttShortcut ?? ForegroundPttShortcut.Unassigned;
        SaveForegroundPttCommand = createCommand(() => ExecuteForegroundPttCommandAsync(
            new(AppearanceCommandOperation.Set, SelectedForegroundPttShortcut), SecurityAuditInitiator.LocalUser));
        CancelForegroundPttCommand = createCommand(() =>
        {
            SelectedForegroundPttShortcut = EffectiveForegroundPttShortcut ?? ForegroundPttShortcut.Unassigned;
            return Task.CompletedTask;
        });
    }

    private void OnForegroundPttChanged(object? sender, EventArgs args) => uiDispatcher.Post(() =>
    {
        OnPropertyChanged(nameof(EffectiveForegroundPttShortcut));
        OnPropertyChanged(nameof(ForegroundPttStatus));
        OnPropertyChanged(nameof(CanChangeForegroundPtt));
    });

    internal async Task ExecuteForegroundPttCommandAsync(ForegroundPttCommand command, SecurityAuditInitiator initiator)
    {
        var service = foregroundPtt;
        var native = foregroundPttNativeLifetime;
        if (service is null || foregroundPttSettingActive || !IsCallMutationHostEligible || IsResponseInteractionPending
            || initiator == SecurityAuditInitiator.LocalUser && !native())
        {
            Transcript = "Foreground shortcut settings require the owning unlocked host and no pending question or approval.";
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify) { Transcript = command.Error!; return; }
        var origin = OriginalOrigin(initiator);
        var admitted = CaptureAudioControlEligibility(origin);
        bool Eligible() => !disposed && admitted()
            && (initiator != SecurityAuditInitiator.LocalUser || ReferenceEquals(native, foregroundPttNativeLifetime) && native());
        foregroundPttSettingActive = true;
        OnPropertyChanged(nameof(CanChangeForegroundPtt));
        try
        {
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var preference = service.Get() ?? throw new InvalidOperationException("Foreground shortcut preference is unconfirmed.");
                if (!await service.SetAsync(command.Shortcut!, preference.Revision, CallPolicyRevision, origin,
                    initiator, communicationPolicy, Eligible, CancellationToken.None))
                { Transcript = "Foreground shortcut setting denied by current call/channel policy."; return; }
                SelectedForegroundPttShortcut = command.Shortcut!;
            }
            if (!Eligible()) { throw new InvalidOperationException("Foreground shortcut setting lost original admission."); }
            Transcript = ForegroundPttStatus;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        {
            ApplicationLog.Error(logger, exception, "Inspecting or changing foreground PTT preference");
            Transcript = "Foreground shortcut not confirmed. Inspect saved state and audit before recovery. " + exception.Message;
        }
        finally
        {
            foregroundPttSettingActive = false;
            OnForegroundPttChanged(this, EventArgs.Empty);
        }
    }
}
