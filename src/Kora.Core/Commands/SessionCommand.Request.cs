using System.Text;
using System.Collections.Immutable;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public const string RequestSyntax =
        "session request review <session-id> <generation> <metadata-revision> [copilot|claude|claude-tools=<exact,ids>] -- <original body> | "
        + "session request confirm <session-id> <review-id> | session request cancel <session-id> <review-id>. "
        + "The body is everything after the single separator space following --, without normalization. "
        + "Review expires after 60 seconds; confirmation consumes it once and preserves the original channel. "
        + "Optional copilot or claude explicitly selects that service; a separate native final-send disclosure is still mandatory. "
        + "claude-tools selects one to six distinct original read-only observation IDs, not provider-native effects. "
        + "Voice requires the current assistant-name prefix. No implicit selected-session, question or approval routing. "
        + "Model/provider submission is unavailable: original-user authority is required; no model submission capability is registered. "
        + ForegroundRequestSyntax;
    public const string ReadableRequestSyntax =
        "session request review active [copilot|claude|claude-tools=<exact,ids>] -- <original body> | "
        + "session request review \"<exact readable name>\" [copilot|claude|claude-tools=<exact,ids>] -- <original body> | "
        + "session request confirm | session request cancel. "
        + "Active must resolve to exactly one Active conversation; names must resolve uniquely. "
        + "Confirmation consumes only the one host-held request review; it never answers another question or approval.";

    public string? RequestBody { get; init; }
    public ConversationMessage? RequestFailedMessage { get; init; }
    public Guid? RequestReviewId { get; init; }
    public SessionRequestReview.ProviderChoice RequestProvider { get; init; }
    public ImmutableArray<string> RequestClaudeTools { get; init; } = [];
    public SessionName? RequestTargetName { get; init; }
    public bool RequestActiveTarget { get; init; }
    public bool RequestUsesPendingReview { get; init; }
    public bool RequestForegroundTarget { get; init; }
    public bool RequestActivationPrefixed { get; init; }
    public const string ForegroundRequestSyntax =
        "ask this conversation <original body>. Review the complete message and current foreground conversation, "
        + "then separately use session request confirm or session request cancel. "
        + "One visible eligible Active conversation presenter is required. No new conversation or hosted fallback.";

    private static SessionCommand? ParseForegroundRequest(string text)
    {
        const string foreground = "ask this conversation";
        if (text.Equals("ask this", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("ask this", StringComparison.OrdinalIgnoreCase) && text.Length > 8
                && char.IsWhiteSpace(text[8]))
        {
            if (!text.StartsWith(foreground + " ", StringComparison.OrdinalIgnoreCase))
            { return new(SessionCommandOperation.Invalid, Error: ForegroundRequestSyntax); }
            var body = text[(foreground.Length + 1)..];
            try { ConversationText.Validate(body, submission: true); }
            catch (InvalidDataException) { return new(SessionCommandOperation.Invalid, Error: ForegroundRequestSyntax); }
            catch (ConversationCredentialException) { return new(SessionCommandOperation.Invalid, Error: ForegroundRequestSyntax); }
            return new(SessionCommandOperation.RequestReview) { RequestBody = body, RequestForegroundTarget = true };
        }
        return null;
    }

    private static SessionCommand? ParseRequest(string text)
    {
        if (ParseForegroundRequest(text) is { } foreground) { return foreground; }
        var words = text.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || !words[0].Equals("session", StringComparison.OrdinalIgnoreCase)
            || !words[1].Equals("request", StringComparison.OrdinalIgnoreCase))
        { return null; }
        static SessionCommand Invalid() => new(SessionCommandOperation.Invalid, Error: RequestSyntax + " " + ReadableRequestSyntax);
        static bool Provider(string value, out SessionRequestReview.ProviderChoice provider, out ImmutableArray<string> tools)
        {
            tools = [];
            if (value.StartsWith("claude-tools=", StringComparison.OrdinalIgnoreCase))
            {
                provider = SessionRequestReview.ProviderChoice.Claude;
                try { tools = ClaudeObservationTools.Constrain(value[13..].Split(',')).ToolIds; return true; }
                catch (InvalidDataException) { return false; }
            }
            provider = value.Equals("claude", StringComparison.OrdinalIgnoreCase)
                ? SessionRequestReview.ProviderChoice.Claude : SessionRequestReview.ProviderChoice.Copilot;
            return value.Equals("claude", StringComparison.OrdinalIgnoreCase) || value.Equals("copilot", StringComparison.OrdinalIgnoreCase);
        }
        var separator = RequestSeparator(text);
        var header = separator < 0 ? text.TrimEnd() : text[..separator];
        if (Encoding.UTF8.GetByteCount(header) > MaximumInputBytes || header.Any(char.IsControl))
        { return Invalid(); }
        var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && parts[2].Equals("help", StringComparison.OrdinalIgnoreCase) && separator < 0)
        { return new(SessionCommandOperation.Help); }
        if (parts.Length == 3 && separator < 0
            && parts[2].ToLowerInvariant() is "confirm" or "cancel")
        {
            return new(parts[2].Equals("confirm", StringComparison.OrdinalIgnoreCase)
                ? SessionCommandOperation.RequestConfirm : SessionCommandOperation.RequestCancel)
            { RequestUsesPendingReview = true };
        }
        var readable = header.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (readable.Length == 4 && readable[2].Equals("review", StringComparison.OrdinalIgnoreCase) && separator >= 0)
        {
            var targetText = readable[3].TrimEnd();
            var provider = SessionRequestReview.ProviderChoice.Default;
            ImmutableArray<string> tools = [];
            var lastSpace = targetText.LastIndexOf(' ');
            if (lastSpace >= 0 && Provider(targetText[(lastSpace + 1)..], out var explicitProvider, out tools))
            { provider = explicitProvider; targetText = targetText[..lastSpace].TrimEnd(); }
            var active = targetText.Equals("active", StringComparison.OrdinalIgnoreCase);
            SessionName? name = null;
            if (!active && targetText.StartsWith('"') && targetText.EndsWith('"') && targetText.Length > 2)
            {
                var value = targetText[1..^1];
                if (value.Replace("\"\"", string.Empty, StringComparison.Ordinal).Contains('"')) { return Invalid(); }
                try { name = new(value.Replace("\"\"", "\"", StringComparison.Ordinal)); }
                catch (InvalidDataException) { return Invalid(); }
            }
            if (active || name is not null)
            {
                var body = text[(separator + 4)..];
                try { ConversationText.Validate(body, submission: true); }
                catch (InvalidDataException) { return Invalid(); }
                catch (ConversationCredentialException) { return Invalid(); }
                return new(SessionCommandOperation.RequestReview)
                { RequestBody = body, RequestProvider = provider, RequestClaudeTools = tools, RequestActiveTarget = active, RequestTargetName = name };
            }
        }
        if (parts.Length < 5 || !ExactId(parts[3], out var session)) { return Invalid(); }
        var exactProvider = SessionRequestReview.ProviderChoice.Default;
        ImmutableArray<string> exactTools = [];
        if ((parts.Length == 6 || parts.Length == 7 && Provider(parts[6], out exactProvider, out exactTools))
            && parts[2].Equals("review", StringComparison.OrdinalIgnoreCase) && separator >= 0
            && Revision(parts[4], true, out var generation) && Revision(parts[5], false, out var metadata))
        {
            var body = text[(separator + 4)..];
            try { ConversationText.Validate(body, submission: true); }
            catch (InvalidDataException) { return Invalid(); }
            catch (ConversationCredentialException) { return Invalid(); }
            return new(SessionCommandOperation.RequestReview, session, generation, metadata)
            {
                RequestBody = body,
                RequestProvider = exactProvider,
                RequestClaudeTools = exactTools,
            };
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

    private static int RequestSeparator(string text)
    {
        var quoted = false;
        for (var index = 0; index < text.Length - 3; index++)
        {
            if (text[index] == '"')
            {
                if (quoted && text[index + 1] == '"') { index++; }
                else { quoted = !quoted; }
            }
            else if (!quoted && text.AsSpan(index, 4).SequenceEqual(" -- ")) { return index; }
        }
        return -1;
    }
}
