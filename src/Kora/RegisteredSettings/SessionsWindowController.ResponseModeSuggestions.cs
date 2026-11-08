using Kora.Application.ViewModels;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    partial void BindAdditionalRegisteredSettingSuggestions(SessionsViewModel createdModel, MainViewModel host)
    {
    createdModel.BindVoiceCaptureTimingSuggestions(host.ReviewVoiceCaptureTimingSuggestionAsync,
        host.ConfirmVoiceCaptureTimingSuggestionAsync);
        createdModel.BindResponseModeSuggestions(host.ReviewResponseModeSuggestionAsync,
            (review, token) => host.ConfirmResponseModeSuggestionAsync(review, token));
        createdModel.BindWindowsSpeechRateSuggestions(host.ReviewWindowsSpeechRateSuggestionAsync,
            (review, token) => host.ConfirmWindowsSpeechRateSuggestionAsync(review, token));
        createdModel.BindQuietHoursSuggestions(host.ReviewQuietHoursSuggestionAsync, host.ConfirmQuietHoursSuggestionAsync);
        createdModel.BindOrdinaryTurnDeadlineSuggestions(host.ReviewOrdinaryTurnDeadlineSuggestionAsync, host.ConfirmOrdinaryTurnDeadlineSuggestionAsync);
        createdModel.BindDetailPresentationSuggestions(host.ReviewDetailPresentationSuggestionAsync, host.ConfirmDetailPresentationSuggestionAsync);
        createdModel.BindInstalledSpeechVoiceSuggestions(host.ReviewInstalledSpeechVoiceSuggestionAsync,
            (review, token) => host.ConfirmInstalledSpeechVoiceSuggestionAsync(review, token));
    }
}
