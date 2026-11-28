using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private RoutingAdviceConfigurationService? routingAdvice;
    private Func<bool> routingAdviceNativeLifetime = static () => false;
    private bool routingAdviceSettingActive;
    public bool IsRoutingAdviceEnabled => routingAdvice?.Get()?.Enabled == true;
    public string RoutingAdviceStatus => routingAdvice?.Get() is { } state
        ? state.Enabled ? "On. Voice requests may use local metadata-only advice to preselect an Active chat."
            : "Off (default). No model advice for voice destinations; the last selected Active chat is offered when available."
        : "Routing advice unavailable. Inspect saved preference and audit; no standing model disclosure is authorized.";
    public bool CanChangeRoutingAdvice => routingAdvice?.Get() is not null && !routingAdviceSettingActive
        && routingAdviceNativeLifetime() && IsCallMutationHostEligible && !IsResponseInteractionPending;
    public AsyncCommand EnableRoutingAdviceCommand { get; private set; } = null!;
    public AsyncCommand DisableRoutingAdviceCommand { get; private set; } = null!;

    public void BindRoutingAdviceConfiguration(RoutingAdviceConfigurationService service)
    {
        routingAdvice = service;
        try { service.Observe(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        { ApplicationLog.Error(logger, exception, "Reading routing advice preference"); }
        void Failed(Exception exception)
        {
            ApplicationLog.Error(logger, exception, "Changing routing advice preference");
            ShowFailure("Routing advice setting not confirmed.", "Inspect saved state and audit before recovery. No standing model disclosure was authorized.");
        }
        EnableRoutingAdviceCommand = new(() => SetRoutingAdviceAsync(true), Failed);
        DisableRoutingAdviceCommand = new(() => SetRoutingAdviceAsync(false), Failed);
        NotifyRoutingAdviceChanged();
    }

    public void BindRoutingAdviceNativeLifetime(Func<bool> lifetime)
    {
        routingAdviceNativeLifetime = lifetime;
        OnPropertyChanged(nameof(CanChangeRoutingAdvice));
    }

    public Func<bool>? CaptureRoutingAdviceConsent()
    {
        var consent = routingAdvice?.Get();
        return consent?.Enabled == true ? () => !disposed && routingAdvice!.Get() == consent : null;
    }

    private void NotifyRoutingAdviceChanged()
    {
        OnPropertyChanged(nameof(IsRoutingAdviceEnabled));
        OnPropertyChanged(nameof(RoutingAdviceStatus));
        OnPropertyChanged(nameof(CanChangeRoutingAdvice));
    }

    private async Task SetRoutingAdviceAsync(bool enabled)
    {
        if (routingAdvice!.Get() is not { } state || !CanChangeRoutingAdvice)
        {
            ShowFailure("Routing advice setting unavailable.", "Use the current unlocked owning Settings view with no pending approval.");
            return;
        }
        var native = routingAdviceNativeLifetime;
        var origin = OriginalOrigin(SecurityAuditInitiator.LocalUser);
        var admitted = CaptureAudioControlEligibility(origin);
        bool Eligible() => !disposed && admitted() && ReferenceEquals(native, routingAdviceNativeLifetime) && native();
        routingAdviceSettingActive = true;
        NotifyRoutingAdviceChanged();
        try
        {
            if (!await routingAdvice.SetAsync(enabled, state.Revision, CallPolicyRevision, origin,
                SecurityAuditInitiator.LocalUser, communicationPolicy, Eligible, CancellationToken.None))
            { ShowFailure("Routing advice setting denied.", "Current call/channel policy refused the change. Inspect before retrying."); }
        }
        finally
        {
            routingAdviceSettingActive = false;
            NotifyRoutingAdviceChanged();
        }
    }
}
