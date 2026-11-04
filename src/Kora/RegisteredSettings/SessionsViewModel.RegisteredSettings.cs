using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private enum SettingReviewFamily { Appearance, Summary, Volume, ResponseMode, WindowsRate, InstalledVoice, QuietHours }

    public string RegisteredSettingsDescription => "Supported inert suggestions: " + string.Join("; ",
        RegisteredSettingRegistry.Settings.Select(item => $"{item.Id}: "
            + item.SuggestedValues
            + (item.RequiresNativeReview ? " (native review only)" : string.Empty)))
        + ". Set supplies one value; Reset supplies only the exact ID and uses the owner's default. "
        + "Installed voice supports only Set with an exact installed provider and voice pair, not Reset. "
        + "All are shared device-local preferences. Review changes nothing.";

    public bool CanReviewRegisteredSettingSuggestion => CanReviewAppearanceSuggestion
        || CanReviewSpeechSummarySuggestion || CanReviewPlaybackVolumeSuggestion || CanReviewResponseModeSuggestion || CanReviewWindowsSpeechRateSuggestion
        || CanReviewQuietHoursSuggestion || CanReviewInstalledSpeechVoiceSuggestion;
    public bool CanConfirmRegisteredSettingSuggestion => CanConfirmAppearanceSuggestion
        || CanConfirmSpeechSummarySuggestion || CanConfirmPlaybackVolumeSuggestion || CanConfirmResponseModeSuggestion || CanConfirmWindowsSpeechRateSuggestion
        || CanConfirmQuietHoursSuggestion || CanConfirmInstalledSpeechVoiceSuggestion;
    public bool CanCancelRegisteredSettingSuggestion => CanConfirmRegisteredSettingSuggestion
        || !closed && access.CanControl && quietHoursSuggestionIdentity is not null;
    public string RegisteredSettingSuggestionDescription => AppearanceSuggestionDescription
        + SpeechSummarySuggestionDescription + PlaybackVolumeSuggestionDescription + ResponseModeSuggestionDescription + WindowsSpeechRateSuggestionDescription
        + QuietHoursSuggestionDescription + InstalledSpeechVoiceSuggestionDescription;

    public Task ReviewRegisteredSettingSuggestionAsync(ConversationMessage message) =>
        ReviewRegisteredSettingSuggestionAsync(message, null);

    private Task ReviewRegisteredSettingSuggestionAsync(ConversationMessage message, SettingReviewFamily? expected)
    {
        var originalInput = Kora.Core.Diagnostics.HostActivity.Current;
        return RunConversationAsync(async (target, live) =>
        {
            CancelRegisteredSettingSuggestion(reportCancellation: false);
            if (!access.CanControl || !target.Authority.IsActive || message.Generation != target.Authority.Generation
                || !ObservedConversationMessages.Any(row => ReferenceEquals(row, message)))
            { throw new InvalidOperationException("Select an exact admitted answer from this Active conversation's current page for setting review."); }
            var proposal = RegisteredSettingSuggestions.Parse(message.AssistantResponse?.Body
                ?? throw new InvalidDataException("The selected message has no setting suggestion."));
            var family = proposal switch
            {
                RegisteredSettingProposal.Appearance => SettingReviewFamily.Appearance,
                RegisteredSettingProposal.Summary => SettingReviewFamily.Summary,
                RegisteredSettingProposal.Volume => SettingReviewFamily.Volume,
                RegisteredSettingProposal.ResponseMode => SettingReviewFamily.ResponseMode,
                RegisteredSettingProposal.WindowsRate => SettingReviewFamily.WindowsRate,
                RegisteredSettingProposal.QuietHours => SettingReviewFamily.QuietHours,
                RegisteredSettingProposal.Reset { Setting: RegisteredSetting.QuietHours } => SettingReviewFamily.QuietHours,
                RegisteredSettingProposal.Reset reset when reset.Descriptor.RequiresNativeReview => SettingReviewFamily.Appearance,
                RegisteredSettingProposal.Reset { Setting: RegisteredSetting.ResponseTimeout } => SettingReviewFamily.Appearance,
                RegisteredSettingProposal.Reset { Setting: RegisteredSetting.SummaryWords or RegisteredSetting.SummarySentences } => SettingReviewFamily.Summary,
                RegisteredSettingProposal.Reset { Setting: RegisteredSetting.PlaybackVolume } => SettingReviewFamily.Volume,
                RegisteredSettingProposal.Reset { Setting: RegisteredSetting.ResponseMode } => SettingReviewFamily.ResponseMode,
                RegisteredSettingProposal.Reset { Setting: RegisteredSetting.WindowsSpeechRate } => SettingReviewFamily.WindowsRate,
                RegisteredSettingProposal.InstalledVoice => SettingReviewFamily.InstalledVoice,
                _ => throw new InvalidDataException("The setting suggestion is not registered."),
            };
            if (expected is not null && family != expected)
            { throw new InvalidDataException("The exact suggestion belongs to a different setting family; nothing was applied."); }
            var page = conversationPage;
            var completed = completedConversationMessage;
            var quietIdentity = family == SettingReviewFamily.QuietHours ? new object() : null;
            quietHoursSuggestionIdentity = quietIdentity;
            var native = quietIdentity is null ? static () => true
                : (CaptureQuietHoursNativeLifetime ?? throw new InvalidOperationException("A current native quiet-hours surface is required."))();
            bool Current() => live() && access.CanControl && !conversationSourceHeld
                && (quietIdentity is null || ReferenceEquals(quietIdentity, quietHoursSuggestionIdentity) && native())
                && ReferenceEquals(page, conversationPage) && ReferenceEquals(completed, completedConversationMessage);
            async Task Validate(CancellationToken token)
            {
                _ = await conversation!.ReadSettingSuggestionAsync(target, message, Current, token).ConfigureAwait(false);
            }
            NotifyRegisteredSettings();
            switch (family)
            {
                case SettingReviewFamily.QuietHours:
                    RequireQuietHoursNativeInput(originalInput);
                    var quietReview = await (reviewQuietHoursSuggestion
                        ?? throw new InvalidOperationException("Native quiet-hours review is unavailable."))(message, Current, Validate, lifetime.Token);
                    if (!Current())
                    {
                        quietReview.Dispose();
                        throw new OperationCanceledException("The exact quiet-hours review was retired.");
                    }
                    quietHoursSuggestionReview = quietReview;
                    break;
                case SettingReviewFamily.Appearance:
                    appearanceReview = await (reviewAppearanceSuggestion
                        ?? throw new InvalidOperationException("Native appearance review is unavailable."))(message, Current, Validate, lifetime.Token);
                    break;
                case SettingReviewFamily.Summary:
                    speechSummaryReview = await (reviewSpeechSummarySuggestion
                        ?? throw new InvalidOperationException("Speech limit review is unavailable."))(message, Current, Validate, lifetime.Token);
                    break;
                case SettingReviewFamily.Volume:
                    playbackVolumeReview = await (reviewPlaybackVolumeSuggestion
                        ?? throw new InvalidOperationException("Kora volume review is unavailable."))(message, Current, Validate, lifetime.Token);
                    break;
                case SettingReviewFamily.ResponseMode:
                    responseModeReview = await (reviewResponseModeSuggestion
                        ?? throw new InvalidOperationException("Response-mode review is unavailable."))(message, Current, Validate, lifetime.Token);
                    break;
                case SettingReviewFamily.WindowsRate:
                    windowsSpeechRateReview = await (reviewWindowsSpeechRateSuggestion
                        ?? throw new InvalidOperationException("Windows rate review is unavailable."))(message, Current, Validate, lifetime.Token);
                    break;
                case SettingReviewFamily.InstalledVoice:
                    installedVoiceReview = await (reviewInstalledVoiceSuggestion
                        ?? throw new InvalidOperationException("Installed voice review is unavailable."))(message, Current, Validate, lifetime.Token);
                    break;
            }
            if (!Current())
            {
                CancelRegisteredSettingSuggestion(reportCancellation: false);
                throw new OperationCanceledException("The exact setting suggestion source was revoked.");
            }
            conversationStatus = "Review exact current/proposed values and shared scope below. Nothing changed; confirmation is separate.";
        });
    }

    public Task ConfirmRegisteredSettingSuggestionAsync() => appearanceReview is not null
        ? ConfirmAppearanceSuggestionAsync()
        : quietHoursSuggestionReview is not null ? ConfirmQuietHoursSuggestionAsync()
        : windowsSpeechRateReview is not null ? ConfirmWindowsSpeechRateSuggestionAsync()
        : installedVoiceReview is not null ? ConfirmInstalledSpeechVoiceSuggestionAsync()
        : speechSummaryReview is not null ? ConfirmSpeechSummarySuggestionAsync()
        : responseModeReview is not null ? ConfirmResponseModeSuggestionAsync() : ConfirmPlaybackVolumeSuggestionAsync();

    public void CancelRegisteredSettingSuggestion(bool reportCancellation = true)
    {
        CancelQuietHoursSuggestion();
        CancelAppearanceSuggestion(reportCancellation: false);
        CancelSpeechSummarySuggestion(reportCancellation: false);
        CancelPlaybackVolumeSuggestion(reportCancellation: false);
        CancelResponseModeSuggestion();
        CancelWindowsSpeechRateSuggestion();
        CancelInstalledSpeechVoiceSuggestion();
        if (reportCancellation)
        {
            conversationStatus = "Setting review cancelled. No new setting write was requested.";
            OnPropertyChanged(nameof(ConversationStatus));
        }
        NotifyRegisteredSettings();
    }

    private void NotifyRegisteredSettings()
    {
        if (responseModeReview is { Available: false }) { CancelResponseModeSuggestion(); }
        if (windowsSpeechRateReview is { Available: false }) { CancelWindowsSpeechRateSuggestion(); }
        if (quietHoursSuggestionReview is { Available: false }) { CancelQuietHoursSuggestion(); }
        if (installedVoiceReview is { Available: false }) { CancelInstalledSpeechVoiceSuggestion(); }
        OnPropertyChanged(nameof(CanReviewRegisteredSettingSuggestion));
        OnPropertyChanged(nameof(CanConfirmRegisteredSettingSuggestion));
        OnPropertyChanged(nameof(CanCancelRegisteredSettingSuggestion));
        OnPropertyChanged(nameof(RegisteredSettingSuggestionDescription));
    }
}
