namespace Kora.Application.Skills;

public sealed record ObservationWorkflowCommand(string Operation, string? Id = null, string? Version = null, string? Literal = null)
{
    public const string Syntax = "owned skill workflow editor|create <id> <version>|name: <literal>|description: <literal>|definition: <JSON steps>|validate|save|list|reopen <id> <version>|targets|next-targets|target <session-id>|request: <literal>|review <id> <version>|enable|invoke|approve-step|deny-step|status|cancel|close";

    public static ObservationWorkflowCommand Parse(string input)
    {
        foreach (var field in new[] { "name", "description", "definition", "request" })
        {
            var prefix = field + ": ";
            if (input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            { return new(field, Literal: input[prefix.Length..]); }
        }
        if (input.Any(char.IsControl)) { return new("Invalid"); }
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
        {
            var operation = words[0].ToLowerInvariant();
            return new(operation is "editor" or "validate" or "save" or "list" or "targets" or "next-targets"
                or "enable" or "invoke" or "approve-step" or "deny-step" or "status" or "cancel" or "close" ? operation : "Invalid");
        }
        if (words.Length == 2 && words[0].Equals("target", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(words[1], "D", out var id) && string.Equals(id.ToString("D"), words[1], StringComparison.Ordinal))
        { return new("target", Literal: words[1]); }
        if (words.Length == 3 && words[0].ToLowerInvariant() is "create" or "reopen" or "review")
        {
            try { Kora.Core.Skills.OwnedDeclarativeSkillDocument.RequireIdentity(words[1], words[2]); }
            catch (InvalidDataException) { return new("Invalid"); }
            return new(words[0].ToLowerInvariant(), words[1], words[2]);
        }
        return new("Invalid");
    }
}
