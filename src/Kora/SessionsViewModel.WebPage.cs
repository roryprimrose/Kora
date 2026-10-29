using Kora.Application.Network;
using Kora.Core.Network;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    internal WebPageApprovalPresenter? WebPagePresenter { get; set; }

    public Task SubmitWebPageAsync()
    {
        var command = WebPageCommand.Parse(composerBody, "Kora");
        if (command?.Address is not { } address || command.Error is not null)
        {
            conversationStatus = "For a local summary, enter get web page <absolute HTTP or HTTPS address>. "
                + "The destination requires an exact permission grant; a matching saved grant is reused. Nothing was submitted.";
            NotifyConversation();
            return Task.CompletedTask;
        }
        _ = WebPageAccessBinding.DestinationDigest(address);
        return SubmitConversationCoreAsync(allowQuestion: false, pageAddress: address);
    }
}
