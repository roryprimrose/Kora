using Kora.Core.Configuration;

namespace Kora.Core.Context;

public sealed record ClipboardSnapshotStatusCommand(bool IsActivated, bool IsHelp, Guid SnapshotId)
{
    public const string Syntax = "clipboard snapshot status <snapshot-id> | clipboard snapshot help. "
        + "Use the exact nonempty lowercase hyphenated host snapshot ID from the native preview. "
        + "Status checks only that immutable snapshot; it never reads the system clipboard or admits source use.";

    public bool IsValid => IsHelp || SnapshotId != Guid.Empty;
    public static IEnumerable<string> DiscoveryPhrases => ["clipboard snapshot help", "clipboard snapshot status"];

    public static ClipboardSnapshotStatusCommand? Parse(string input, string assistantName)
    {
        var text = input.AsSpan();
        var name = AssistantNameRules.Normalize(assistantName);
        var activated = text.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && text.Length > name.Length && (text[name.Length] == ',' || text[name.Length] == ' ');
        if (activated) { text = text[name.Length..].TrimStart(" ,"); }

        // Reserve malformed spelling/separators before the lossy general command normalizer.
        var reserved = text.TrimStart();
        if (!activated && reserved.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && reserved.Length > name.Length && !char.IsLetterOrDigit(reserved[name.Length]))
        {
            reserved = reserved[name.Length..];
        }
        if (!TakeWord(ref reserved, "clipboard") || !TakeWord(ref reserved, "snapshot")) { return null; }
        const string prefix = "clipboard snapshot status ";
        if (text.Equals("clipboard snapshot help", StringComparison.OrdinalIgnoreCase))
        {
            return new(activated, true, Guid.Empty);
        }
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = text[prefix.Length..];
            if (id.Length == 36 && Guid.TryParseExact(id, "D", out var parsed)
                && parsed != Guid.Empty && id.SequenceEqual(parsed.ToString("D")))
            {
                return new(activated, false, parsed);
            }
        }
        return new(activated, false, Guid.Empty);
    }

    private static bool TakeWord(ref ReadOnlySpan<char> text, string word)
    {
        while (!text.IsEmpty && !char.IsLetterOrDigit(text[0])) { text = text[1..]; }
        if (!text.StartsWith(word, StringComparison.OrdinalIgnoreCase)) { return false; }
        text = text[word.Length..];
        return text.IsEmpty || !char.IsLetterOrDigit(text[0]);
    }
}
