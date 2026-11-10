using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    internal Func<SessionWorkspaceEntry, Func<bool>, Task>? MetadataAnswerRequested { get; set; }
    public Task OpenMetadataAnswerAsync() => RunConversationAsync(async (target, live) =>
    {
        await (MetadataAnswerRequested ?? throw new InvalidOperationException("The reviewed metadata surface is unavailable."))
            (target, live);
        conversationStatus = "Metadata review is a separate local operation. No inspected session was selected or renewed.";
    });
}
