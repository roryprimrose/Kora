using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public const string SpokenNameRouteSyntax =
        "<current assistant name>, in <exact Active chat name>, <original body>. "
        + "The longest unique exact name wins; ambiguous or absent names refuse without a fallback. "
        + "The comma after the chat name is optional. Review and confirm separately. "
        + "Requires the literal assistant-name prefix for typed or push-to-talk input; bare wake bodies are unchanged.";

    public string? RequestSpokenNameRoute { get; init; }

    private static SessionCommand? ParseSpokenNameRoute(string text, bool activationPrefixed)
    {
        if (!activationPrefixed || !(text.Equals("in", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("in ", StringComparison.OrdinalIgnoreCase)))
        { return null; }
        var route = text.Length > 3 ? text[3..] : string.Empty;
        var measurement = ConversationText.Measure(route);
        if (measurement.IsWhiteSpace || measurement.Utf8Bytes is null || measurement.HasUnsupportedContent
            || route.Length > ConversationText.MaximumUtf16Characters + SessionName.MaximumScalars * 2 + 2)
        { return new(SessionCommandOperation.Invalid, Error: SpokenNameRouteSyntax); }
        try { ConversationText.RequireNoCredentials(route); }
        catch (ConversationCredentialException)
        { return new(SessionCommandOperation.Invalid, Error: SpokenNameRouteSyntax); }
        return new(SessionCommandOperation.RequestReview) { RequestSpokenNameRoute = route };
    }
}
