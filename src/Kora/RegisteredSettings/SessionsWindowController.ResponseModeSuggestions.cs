using Kora.Application.ViewModels;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    partial void BindAdditionalRegisteredSettingSuggestions(SessionsViewModel createdModel, MainViewModel host)
    {
        createdModel.BindResponseModeSuggestions(host.ReviewResponseModeSuggestionAsync,
            (review, token) => host.ConfirmResponseModeSuggestionAsync(review, token));
    }
}
