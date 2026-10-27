using Kora.Application.ViewModels;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    // First-party setting families supply the optional compiled binding in their owned folder.
    partial void BindAdditionalRegisteredSettingSuggestions(SessionsViewModel createdModel, MainViewModel host);
}
