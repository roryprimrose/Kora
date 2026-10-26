using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ConversationalReplyConfigurationService? conversationalReplies;
    private Func<bool> conversationalReplyNativeLifetime = static () => false;
    private bool conversationalReplySettingActive;
    public bool AreConversationalRepliesEnabled => conversationalReplies?.Get()?.Enabled == true;
    public string ConversationalReplyStatus => conversationalReplies?.Get() is { } state
        ? state.Enabled ? "On. A new exact foreground host question is spoken before one bounded reply. Voice consent and readiness are still required."
            : "Off (default). Use Answer by voice or Hear question, then answer by voice explicitly."
        : "Conversational replies unavailable. Inspect saved preference and audit before recovery; manual/native answers remain available.";
    public bool CanChangeConversationalReplies => conversationalReplies?.Get() is not null && !conversationalReplySettingActive
        && conversationalReplyNativeLifetime() && IsCallMutationHostEligible && !IsResponseInteractionPending;
    public AsyncCommand EnableConversationalRepliesCommand { get; private set; } = null!;
    public AsyncCommand DisableConversationalRepliesCommand { get; private set; } = null!;

    public void BindConversationalReplyNativeLifetime(Func<bool> lifetime)
    {
        conversationalReplyNativeLifetime = lifetime;
        OnPropertyChanged(nameof(CanChangeConversationalReplies));
    }

    private void InitializeConversationalReplies(ConversationalReplyConfigurationService? service, Func<Func<Task>, AsyncCommand> createCommand)
    {
        conversationalReplies = service;
        if (service is not null)
        {
            try { service.Observe(); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            { ApplicationLog.Error(logger, exception, "Reading conversational reply preference"); }
            service.Changed += OnConversationalReplyPreferenceChanged;
        }
        EnableConversationalRepliesCommand = createCommand(() => SetConversationalRepliesAsync(true));
        DisableConversationalRepliesCommand = createCommand(() => SetConversationalRepliesAsync(false));
    }

    private void OnConversationalReplyPreferenceChanged(object? sender, EventArgs args)
    {
        questionVoiceReplies?.Revalidate();
        uiDispatcher.Post(() =>
        {
            OnPropertyChanged(nameof(AreConversationalRepliesEnabled));
            OnPropertyChanged(nameof(ConversationalReplyStatus));
            OnPropertyChanged(nameof(CanChangeConversationalReplies));
        });
    }

    private async Task SetConversationalRepliesAsync(bool enabled)
    {
        var service = conversationalReplies;
        var native = conversationalReplyNativeLifetime;
        if (service?.Get() is not { } state || !CanChangeConversationalReplies)
        {
            ShowFailure("Conversational reply setting unavailable.", "Use the current unlocked owning Settings window with no pending question or approval.");
            return;
        }
        var origin = OriginalOrigin(SecurityAuditInitiator.LocalUser);
        var admitted = CaptureAudioControlEligibility(origin);
        bool Eligible() => !disposed && admitted() && ReferenceEquals(native, conversationalReplyNativeLifetime) && native();
        conversationalReplySettingActive = true;
        OnPropertyChanged(nameof(CanChangeConversationalReplies));
        try
        {
            if (!await service.SetAsync(enabled, state.Revision, CallPolicyRevision, origin,
                SecurityAuditInitiator.LocalUser, communicationPolicy, Eligible, CancellationToken.None))
            { ShowFailure("Conversational reply setting denied.", "Current call/channel policy refused the change; inspect before retrying."); }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            ApplicationLog.Error(logger, exception, "Changing conversational reply preference");
            ShowFailure("Conversational reply setting not confirmed.", "Automatic replies remain unavailable. Inspect saved state and audit before recovery.");
        }
        finally
        {
            conversationalReplySettingActive = false;
            OnConversationalReplyPreferenceChanged(this, EventArgs.Empty);
        }
    }
}
