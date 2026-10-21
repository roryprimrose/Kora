using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    public string ComposerDraftSizeFeedback => Kora.Application.Presentation.ComposerDraftSizeFeedback.Format(
        !closed && access.CanInspect ? ConversationText.Measure(composerBody) : null);
}
