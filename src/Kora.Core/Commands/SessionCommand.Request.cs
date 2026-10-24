using System.Text;

using Kora.Core.Conversation;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public const string RequestSyntax =
        "session request review <session-id> <generation> <metadata-revision> -- <original body> | "
        + "session request confirm <session-id> <review-id> | session request cancel <session-id> <review-id>. "
        + "The body is everything after the single separator space following --, without normalization. "
        + "Review expires after 60 seconds; confirmation consumes it once and preserves the original channel. "
        + "Voice requires the current assistant-name prefix. No name, selected-session, question or approval routing. "
        + "Model/provider submission is unavailable: original-user authority is required; no model submission capability is registered.";

    public string? RequestBody { get; init; }
    public Guid? RequestReviewId { get; init; }

    private static SessionCommand? ParseRequest(string text)
    {
        var words = text.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || !words[0].Equals("session", StringComparison.OrdinalIgnoreCase)
            || !words[1].Equals("request", StringComparison.OrdinalIgnoreCase))
        { return null; }
        static SessionCommand Invalid() => new(SessionCommandOperation.Invalid, Error: RequestSyntax);
        var separator = text.IndexOf(" -- ", StringComparison.Ordinal);
        var header = separator < 0 ? text.TrimEnd() : text[..separator];
        if (Encoding.UTF8.GetByteCount(header) > MaximumInputBytes || header.Any(char.IsControl))
        { return Invalid(); }
        var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && parts[2].Equals("help", StringComparison.OrdinalIgnoreCase) && separator < 0)
        { return new(SessionCommandOperation.Help); }
        if (parts.Length < 5 || !ExactId(parts[3], out var session)) { return Invalid(); }
        if (parts.Length == 6 && parts[2].Equals("review", StringComparison.OrdinalIgnoreCase) && separator >= 0
            && Revision(parts[4], true, out var generation) && Revision(parts[5], false, out var metadata))
        {
            var body = text[(separator + 4)..];
            try { ConversationText.Validate(body, submission: true); }
            catch (InvalidDataException) { return Invalid(); }
            catch (ConversationCredentialException) { return Invalid(); }
            return new(SessionCommandOperation.RequestReview, session, generation, metadata) { RequestBody = body };
        }
        if (parts.Length == 5 && separator < 0 && ExactId(parts[4], out var review))
        {
            if (parts[2].Equals("confirm", StringComparison.OrdinalIgnoreCase))
            { return new(SessionCommandOperation.RequestConfirm, session) { RequestReviewId = review }; }
            if (parts[2].Equals("cancel", StringComparison.OrdinalIgnoreCase))
            { return new(SessionCommandOperation.RequestCancel, session) { RequestReviewId = review }; }
        }
        return Invalid();
    }
}
