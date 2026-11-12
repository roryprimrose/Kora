using Kora.Application.ViewModels;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    // First-party setting families supply the optional compiled binding in their owned folder.
    partial void BindAdditionalRegisteredSettingSuggestions(SessionsViewModel createdModel, MainViewModel host);

    private static void BindSettingSuggestionCollection(SessionsViewModel model, MainViewModel host)
    {
        model.ReviewCollectionAppearance = host.ReviewAppearanceSuggestionAsync;
        model.ReviewCollectionSummary = host.ReviewSpeechSummarySuggestionAsync;
        model.ReviewCollectionVolume = host.ReviewPlaybackVolumeSuggestionAsync;
        model.ReviewCollectionResponseMode = host.ReviewResponseModeSuggestionAsync;
        model.ReviewCollectionWindowsRate = host.ReviewWindowsSpeechRateSuggestionAsync;
        model.ReviewCollectionInstalledVoice = host.ReviewInstalledSpeechVoiceSuggestionAsync;
        model.ReviewCollectionCaptureTiming = host.ReviewVoiceCaptureTimingSuggestionAsync;
        model.ReviewCollectionQuietHours = host.ReviewQuietHoursSuggestionAsync;
        model.ReviewCollectionDetailPresentation = host.ReviewDetailPresentationSuggestionAsync;
        model.ReviewCollectionOrdinaryDeadline = host.ReviewOrdinaryTurnDeadlineSuggestionAsync;
    }
}
