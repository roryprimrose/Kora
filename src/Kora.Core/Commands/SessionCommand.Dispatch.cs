namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public const string ConversationDispatchSyntax =
        "session dispatch status <session-id> | session dispatch pause <session-id> | session dispatch resume <session-id>. "
        + "Targets one exact existing Active ordinary conversation, never the diagnostic queue. "
        + "Pause holds only future dispatch, not a turn already dispatching or running. Resume clears only the manual hold; "
        + "original expiry, FIFO, recovery and all authority checks remain. No action approval. Voice requires the current assistant-name prefix.";

    public bool IsConversationDispatch => Operation is SessionCommandOperation.ConversationDispatchStatus
        or SessionCommandOperation.ConversationDispatchPause or SessionCommandOperation.ConversationDispatchResume;

    private static SessionCommand? ParseConversationDispatch(string text, string input)
    {
        if (!text.StartsWith("session dispatch", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (System.Text.Encoding.UTF8.GetByteCount(input) <= MaximumInputBytes && !input.Any(char.IsControl))
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts[1].Equals("dispatch", StringComparison.OrdinalIgnoreCase)
                && ExactId(parts[3], out var id))
            {
                var operation = parts[2].ToLowerInvariant() switch
                {
                    "status" => SessionCommandOperation.ConversationDispatchStatus,
                    "pause" => SessionCommandOperation.ConversationDispatchPause,
                    "resume" => SessionCommandOperation.ConversationDispatchResume,
                    _ => SessionCommandOperation.Invalid,
                };
                if (operation != SessionCommandOperation.Invalid) { return new(operation, id); }
            }
        }
        return new(SessionCommandOperation.Invalid, Error: ConversationDispatchSyntax);
    }
}
