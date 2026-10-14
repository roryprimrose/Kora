using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class DetailWindowController
{
    [LoggerMessage(311, LogLevel.Warning,
        "Native detail clipboard write failed for ItemId {ItemId}, Revision {Revision}, Profile {Profile}.")]
    private static partial void ClipboardFailure(ILogger logger, Guid itemId, long revision, string profile);

    [LoggerMessage(312, LogLevel.Warning, "Retained conversation detail source revoked after FailureType {FailureType}.")]
    private static partial void ConversationSourceFailure(ILogger logger, string failureType);
}
