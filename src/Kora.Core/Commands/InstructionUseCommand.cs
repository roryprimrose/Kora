using System.Text;

using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

/// <summary>Readable original-user selectors; the host binds exact identities at review.</summary>
public sealed record InstructionUseCommand(string Action, string? Source = null, string? Target = null,
    string? Body = null, string? Error = null)
{
    public const string Syntax = "instructions request review \"<owned instruction name>\" in \"<Active conversation name>\" -- <original request> | "
        + "instructions request confirm | instructions request cancel | instructions request stop. "
        + "Names must match uniquely. Double a quote inside a quoted name. Voice requires the current assistant name.";

    public static InstructionUseCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = ArtifactCommandRouter.RemoveActivationPrefix(input.TrimStart(), assistantName);
        const string prefix = "instructions request";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || text.Length > prefix.Length && !char.IsWhiteSpace(text[prefix.Length]))
        { return null; }
        static InstructionUseCommand Invalid() => new("Invalid", Error: Syntax);
        var rest = text[prefix.Length..];
        if (rest.Trim() is { } control && !input.Any(char.IsControl))
        {
            if (control.Equals("confirm", StringComparison.OrdinalIgnoreCase)) { return new("Confirm"); }
            if (control.Equals("cancel", StringComparison.OrdinalIgnoreCase)) { return new("Cancel"); }
            if (control.Equals("stop", StringComparison.OrdinalIgnoreCase)) { return new("Stop"); }
        }
        if (input.Length > ConversationText.MaximumUtf16Characters + 1024
            || !rest.StartsWith(" review ", StringComparison.OrdinalIgnoreCase))
        { return Invalid(); }
        try
        {
            var offset = " review ".Length;
            var source = OwnedInstructionNames.ReadName(ReadQuoted(rest, ref offset));
            if (!rest.AsSpan(offset).StartsWith(" in ", StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
            offset += 4;
            var target = new SessionName(ReadQuoted(rest, ref offset)).Value;
            if (!rest.AsSpan(offset).StartsWith(" -- ", StringComparison.Ordinal)) { return Invalid(); }
            var body = rest[(offset + 4)..];
            ConversationText.Validate(body, submission: true);
            return new("Review", source, target, body);
        }
        catch (InvalidDataException) { return Invalid(); }
        catch (ConversationCredentialException) { return Invalid(); }
    }

    private static string ReadQuoted(string text, ref int offset)
    {
        if (offset >= text.Length || text[offset++] != '"') { throw new InvalidDataException("Use a quoted name."); }
        var value = new StringBuilder();
        while (offset < text.Length)
        {
            var next = text[offset++];
            if (next != '"') { value.Append(next); continue; }
            if (offset < text.Length && text[offset] == '"') { offset++; value.Append('"'); continue; }
            return value.ToString();
        }
        throw new InvalidDataException("Close the quoted name.");
    }
}
