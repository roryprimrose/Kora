using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private enum SettingReviewFamily { Appearance, Summary, Volume, ResponseMode, WindowsRate, InstalledVoice, CaptureTiming, QuietHours, DetailPresentation, OrdinaryDeadline }
    private volatile object? registeredSettingReviewIdentity;

    public string RegisteredSettingsDescription => "Supported inert suggestions: " + string.Join("; ",
        RegisteredSettingRegistry.Settings.Select(item => $"{item.Id}: "
            + item.SuggestedValues
            + (item.RequiresNativeReview ? " (native review only)" : string.Empty)))
        + ". Set supplies one value; Reset supplies only the exact ID and uses the owner's default. "
        + "Installed voice supports only Set with an exact installed provider and voice pair, not Reset. "
        + "All are shared device-local preferences. An answer may contain up to eight unique suggestions. "
        + "Inspect the whole answer, choose one member, review it and separately confirm. No approval applies to all. Review changes nothing.";

    public bool CanReviewRegisteredSettingSuggestion => CanReviewAppearanceSuggestion
        || CanReviewSpeechSummarySuggestion || CanReviewPlaybackVolumeSuggestion || CanReviewResponseModeSuggestion || CanReviewWindowsSpeechRateSuggestion
        || CanReviewQuietHoursSuggestion || CanReviewInstalledSpeechVoiceSuggestion || CanReviewCaptureTimingSuggestion || CanReviewDetailPresentationSuggestion
        || CanReviewOrdinaryDeadlineSuggestion;
    public bool CanConfirmRegisteredSettingSuggestion => CanConfirmAppearanceSuggestion
        || CanConfirmSpeechSummarySuggestion || CanConfirmPlaybackVolumeSuggestion || CanConfirmResponseModeSuggestion || CanConfirmWindowsSpeechRateSuggestion
        || CanConfirmQuietHoursSuggestion || CanConfirmInstalledSpeechVoiceSuggestion || CanConfirmCaptureTimingSuggestion || CanConfirmDetailPresentationSuggestion
        || CanConfirmOrdinaryDeadlineSuggestion;
    public bool CanCancelRegisteredSettingSuggestion => !closed && access.CanInspect && access.CanControl
        && selected?.Authority.IsActive == true && (registeredSettingReviewIdentity is not null || HasRegisteredSettingCollection);
    public string RegisteredSettingSuggestionDescription => AppearanceSuggestionDescription
        + SpeechSummarySuggestionDescription + PlaybackVolumeSuggestionDescription + ResponseModeSuggestionDescription + WindowsSpeechRateSuggestionDescription
        + QuietHoursSuggestionDescription + InstalledSpeechVoiceSuggestionDescription + CaptureTimingSuggestionDescription + DetailPresentationSuggestionDescription
        + OrdinaryDeadlineSuggestionDescription;

    public Task ReviewRegisteredSettingSuggestionAsync(ConversationMessage message) =>
        ReviewRegisteredSettingSuggestionAsync(message, null);

    private Task ReviewRegisteredSettingSuggestionAsync(ConversationMessage message, SettingReviewFamily? expected,
        RegisteredSetting? selectedSetting = null)
    {
        var originalInput = HostActivity.Current;
        return RunConversationAsync(async (target, live) =>
        {
            CancelRegisteredSettingSuggestion(reportCancellation: false);
            var identity = new object();
            registeredSettingReviewIdentity = identity;
            if (!access.CanControl || !target.Authority.IsActive || message.Generation != target.Authority.Generation
                || !ObservedConversationMessages.Any(row => ReferenceEquals(row, message)))
            { throw new InvalidOperationException("Select an exact admitted answer from this Active conversation's current page for setting review."); }
            var answer = message.AssistantResponse?.Body
                ?? throw new InvalidDataException("The selected message has no setting suggestion.");
            if (expected is null && selectedSetting is null)
            {
                var collection = RegisteredSettingSuggestionCollection.Parse(answer);
                if (collection.Members.Count > 1)
                {
                    var sourcePage = conversationPage;
                    var sourceCompletion = completedConversationMessage;
                    bool CollectionCurrent() => live() && access.CanControl && !conversationSourceHeld
                        && ReferenceEquals(sourcePage, conversationPage) && ReferenceEquals(sourceCompletion, completedConversationMessage);
                    _ = await conversation!.ReadSettingSuggestionAsync(target, message, CollectionCurrent, lifetime.Token);
                    message.Validate();
                    if (!CollectionCurrent() || message.Outcome != Kora.Core.Dependencies.ModelTurnOutcome.Succeeded)
                    { throw new InvalidOperationException("Inspect only an exact successful current original answer."); }
                    settingCollectionSource = message;
                    settingCollectionMembers = collection.Members;
                    settingCollectionCurrent = CollectionCurrent;
                    conversationStatus = "No admitted setting suggestion is selected. Complete original answer inspected. Deliberately choose one member, then review its current/proposed values. Nothing changed; no member is selected automatically.";
                    return;
                }
            }
            var proposal = RegisteredSettingSuggestionCollection.ParseSelected(answer, selectedSetting);
            var family = proposal switch
            {
                RegisteredSettingProposal.Appearance => SettingReviewFamily.Appearance,
                RegisteredSettingProposal.Summary => SettingReviewFamily.Summary,
                RegisteredSettingProposal.Volume => SettingReviewFamily.Volume,
                RegisteredSettingProposal.ResponseMode => SettingReviewFamily.ResponseMode,
                RegisteredSettingProposal.WindowsRate => SettingReviewFamily.WindowsRate,
                RegisteredSettingProposal.OrdinaryDeadline => SettingReviewFamily.OrdinaryDeadline,
                RegisteredSettingProposal.Reset { Setting: RegisteredSetting.OrdinaryTurnDeadline } => SettingReviewFamily.OrdinaryDeadline,
                RegisteredSettingProposal.DetailPresentation => SettingReviewFamily.DetailPresentation,
                RegisteredSettingProposal.Reset { Setting: RegisteredSetting.DetailPresentation } => SettingReviewFamily.DetailPresentation,
                RegisteredSettingProposal.QuietHours => SettingReviewFamily.QuietHours,
                RegisteredSettingProposal.Reset { Setting: RegisteredSetting.QuietHours } => SettingReviewFamily.QuietHours,
                RegisteredSettingProposal.CaptureTiming => SettingReviewFamily.CaptureTiming,
                RegisteredSettingProposal.Reset { Setting: RegisteredSetting.CommandStartWait or RegisteredSetting.ReplyStartWait or RegisteredSetting.TrailingSilence or RegisteredSetting.MaximumCapture } =>
                    SettingReviewFamily.CaptureTiming,
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
            var detailNative = family == SettingReviewFamily.DetailPresentation
                ? (CaptureDetailPresentationNativeLifetime ?? throw new InvalidOperationException("A current native Details surface is required."))()
                : static () => true;
            var deadlineNative = family == SettingReviewFamily.OrdinaryDeadline
                ? (CaptureOrdinaryDeadlineNativeLifetime ?? throw new InvalidOperationException("A current native deadline surface is required."))()
                : static () => true;
            bool Current() => ReferenceEquals(identity, registeredSettingReviewIdentity)
                && live() && access.CanControl && !conversationSourceHeld
                && detailNative() && deadlineNative()
                && (quietIdentity is null || ReferenceEquals(quietIdentity, quietHoursSuggestionIdentity) && native())
                && ReferenceEquals(page, conversationPage) && ReferenceEquals(completed, completedConversationMessage);
            async Task Validate(CancellationToken token)
            {
                _ = await conversation!.ReadSettingSuggestionAsync(target, message, Current, token).ConfigureAwait(false);
            }
            Task<T> Review<T>(
                Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, CancellationToken, Task<T>>? single,
                Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, RegisteredSetting?, CancellationToken, Task<T>>? collection) =>
                selectedSetting is null
                    ? (single ?? throw new InvalidOperationException("This setting review is unavailable."))(message, Current, Validate, lifetime.Token)
                    : (collection ?? throw new InvalidOperationException("This selected setting review is unavailable."))(message, Current, Validate, selectedSetting, lifetime.Token);
            NotifyRegisteredSettings();
            switch (family)
            {
                case SettingReviewFamily.OrdinaryDeadline:
                    RequireOrdinaryDeadlineNativeInput(originalInput);
                    var deadlineReview = await Review(reviewOrdinaryDeadlineSuggestion, ReviewCollectionOrdinaryDeadline);
                    if (!Current())
                    {
                        deadlineReview.Dispose();
                        throw new OperationCanceledException("The exact deadline review was retired.");
                    }
                    ordinaryDeadlineSuggestionReview = deadlineReview;
                    break;
                case SettingReviewFamily.DetailPresentation:
                    RequireDetailPresentationNativeInput(originalInput);
                    var detailReview = await Review(reviewDetailPresentationSuggestion, ReviewCollectionDetailPresentation);
                    if (!Current())
                    {
                        detailReview.Dispose();
                        throw new OperationCanceledException("The exact Details review was retired.");
                    }
                    detailPresentationSuggestionReview = detailReview;
                    break;
                case SettingReviewFamily.QuietHours:
                    RequireQuietHoursNativeInput(originalInput);
                    var quietReview = await Review(reviewQuietHoursSuggestion, ReviewCollectionQuietHours);
                    if (!Current())
                    {
                        quietReview.Dispose();
                        throw new OperationCanceledException("The exact quiet-hours review was retired.");
                    }
                    quietHoursSuggestionReview = quietReview;
                    break;
                case SettingReviewFamily.CaptureTiming:
                    if (originalInput is not null && (originalInput.Request.Origin != RequestOrigin.LocalUi
                        || originalInput.Request.InvocationId is not null || originalInput.Activity!.IsStopped
                        || originalInput.Outcome != HostOperationOutcome.Unknown))
                    { throw new InvalidOperationException("Capture timing review requires original native input, not a relabelled model or voice request."); }
                    var timingReview = await Review(reviewCaptureTimingSuggestion, ReviewCollectionCaptureTiming);
                    if (!Current())
                    {
                        timingReview.Dispose();
                        throw new OperationCanceledException("The exact capture timing review was retired.");
                    }
                    captureTimingSuggestionReview = timingReview;
                    break;
                case SettingReviewFamily.Appearance:
                    appearanceReview = await Review(reviewAppearanceSuggestion, ReviewCollectionAppearance);
                    break;
                case SettingReviewFamily.Summary:
                    speechSummaryReview = await Review(reviewSpeechSummarySuggestion, ReviewCollectionSummary);
                    break;
                case SettingReviewFamily.Volume:
                    playbackVolumeReview = await Review(reviewPlaybackVolumeSuggestion, ReviewCollectionVolume);
                    break;
                case SettingReviewFamily.ResponseMode:
                    responseModeReview = await Review(reviewResponseModeSuggestion, ReviewCollectionResponseMode);
                    break;
                case SettingReviewFamily.WindowsRate:
                    windowsSpeechRateReview = await Review(reviewWindowsSpeechRateSuggestion, ReviewCollectionWindowsRate);
                    break;
                case SettingReviewFamily.InstalledVoice:
                    installedVoiceReview = await Review(reviewInstalledVoiceSuggestion, ReviewCollectionInstalledVoice);
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
        : ordinaryDeadlineSuggestionReview is not null ? ConfirmOrdinaryDeadlineSuggestionAsync()
        : detailPresentationSuggestionReview is not null ? ConfirmDetailPresentationSuggestionAsync()
        : quietHoursSuggestionReview is not null ? ConfirmQuietHoursSuggestionAsync()
        : captureTimingSuggestionReview is not null ? ConfirmCaptureTimingSuggestionAsync()
        : windowsSpeechRateReview is not null ? ConfirmWindowsSpeechRateSuggestionAsync()
        : installedVoiceReview is not null ? ConfirmInstalledSpeechVoiceSuggestionAsync()
        : speechSummaryReview is not null ? ConfirmSpeechSummarySuggestionAsync()
        : responseModeReview is not null ? ConfirmResponseModeSuggestionAsync() : ConfirmPlaybackVolumeSuggestionAsync();

    public void CancelRegisteredSettingSuggestion(bool reportCancellation = true)
    {
        ClearSettingSuggestionCollection();
        registeredSettingReviewIdentity = null;
        CancelOrdinaryDeadlineSuggestion();
        CancelDetailPresentationSuggestion();
        CancelQuietHoursSuggestion();
        CancelCaptureTimingSuggestion();
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
        NotifySettingSuggestionCollection();
        if (responseModeReview is { Available: false }) { CancelResponseModeSuggestion(); }
        if (windowsSpeechRateReview is { Available: false }) { CancelWindowsSpeechRateSuggestion(); }
        if (quietHoursSuggestionReview is { Available: false }) { CancelQuietHoursSuggestion(); }
        if (ordinaryDeadlineSuggestionReview is { Available: false }) { CancelOrdinaryDeadlineSuggestion(); }
        if (detailPresentationSuggestionReview is { Available: false }) { CancelDetailPresentationSuggestion(); }
        if (installedVoiceReview is { Available: false }) { CancelInstalledSpeechVoiceSuggestion(); }
        if (!busy && !conversationBusy && !CanConfirmRegisteredSettingSuggestion)
        { registeredSettingReviewIdentity = null; }
        OnPropertyChanged(nameof(CanReviewRegisteredSettingSuggestion));
        OnPropertyChanged(nameof(CanConfirmRegisteredSettingSuggestion));
        OnPropertyChanged(nameof(CanCancelRegisteredSettingSuggestion));
        OnPropertyChanged(nameof(RegisteredSettingSuggestionDescription));
    }
}
