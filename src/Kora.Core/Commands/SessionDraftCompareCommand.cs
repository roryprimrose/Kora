using System.Text;

using Kora.Core.Artifacts;

namespace Kora.Core.Commands;

public sealed record SessionDraftCompareCommand(Guid? SessionId = null, bool Help = false)
{
    public const string Syntax = "session draft compare <session-id> | session draft compare help";
    public const string Boundary = "Native comparison only: the exact session must already be selected in Sessions, "
        + "with its current memory composer and an already explicitly observed saved revision. "
        + "If unavailable, explicitly read exact saved revision first. No implicit reload, selection, save or submission.";

    public static SessionDraftCompareCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = input.Trim();
        // Reserve the literal family before removing a potentially colliding assistant name.
        if (!Reserved(text)) { text = ArtifactCommandRouter.RemoveActivationPrefix(text, assistantName); }
        if (!Reserved(text)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return new(); }
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 4 || !tokens[2].Equals("compare", StringComparison.OrdinalIgnoreCase)) { return new(); }
        if (tokens[3].Equals("help", StringComparison.OrdinalIgnoreCase)) { return new(Help: true); }
        return Guid.TryParseExact(tokens[3], "D", out var id) && id != Guid.Empty
            && string.Equals(tokens[3], id.ToString("D"), StringComparison.Ordinal) ? new(id) : new();
    }

    private static bool Reserved(string text)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 3 && tokens[0].Equals("session", StringComparison.OrdinalIgnoreCase)
            && tokens[1].Equals("draft", StringComparison.OrdinalIgnoreCase)
            && tokens[2].StartsWith("compare", StringComparison.OrdinalIgnoreCase);
    }
}
