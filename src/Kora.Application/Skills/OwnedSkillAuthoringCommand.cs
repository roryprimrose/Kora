using System.Text;
using Kora.Core.Artifacts;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

public sealed record OwnedSkillAuthoringCommand(string Operation, string? Id = null, string? Version = null, string? Literal = null,
    string? InputName = null)
{
    public const string Syntax = "create a skill that <purpose> | owned skill expected-input: <literal> | owned skill expected-output: <literal> | owned skill generate | owned skill create <id> <version> | owned skill name: <literal> | owned skill description: <literal> | owned skill guidance: <literal> | owned skill critique: <literal> | owned skill refine | owned skill cancel-refinement | owned skill tool none|web-page-get | owned skill inputs none|required-literal | owned skill validate | owned skill fixture: <closed-json> | owned skill test | owned skill cancel-test | owned skill save | owned skill list | owned skill reopen <id> <version> | owned skill use <id> <version> | owned skill enable | owned skill targets | owned skill next-targets | owned skill target <session-id> | owned skill request: <literal> | owned skill input <name>: <literal> | owned skill url: <absolute-url> | owned skill review-tool | owned skill review-use | owned skill invoke | owned skill close-use | owned skill cancel";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        Array.AsReadOnly(new[] { "owned skill editor", "owned skill list", "owned skill validate", "owned skill save", "owned skill cancel",
            "owned skill tool none", "owned skill tool web-page-get", "owned skill review-tool",
            "owned skill inputs none", "owned skill inputs required-literal", "owned skill generate", "owned skill refine", "owned skill cancel-refinement",
            "owned skill enable", "owned skill targets", "owned skill next-targets", "owned skill review-use", "owned skill invoke", "owned skill close-use",
            "owned skill test", "owned skill cancel-test",
            "owned skill workflow editor", "owned skill workflow cancel", "owned skill workflow status",
            "owned skill workflow enable", "owned skill workflow invoke", "owned skill workflow approve-step", "owned skill workflow deny-step" });

    public static OwnedSkillAuthoringCommand? Parse(string input, string assistantName)
    {
        var normalized = ArtifactCommandRouter.RemoveActivationPrefix(input.TrimStart(), assistantName);
        const string createPrefix = "create a skill that";
        if (string.Equals(normalized, createPrefix, StringComparison.OrdinalIgnoreCase)) { return new("Invalid"); }
        if (normalized.StartsWith(createPrefix + " ", StringComparison.OrdinalIgnoreCase))
        {
            if (Encoding.UTF8.GetByteCount(input) > SharedSkillSnapshot.MaximumBytes + 512) { return new("Invalid"); }
            var start = input.IndexOf(createPrefix, StringComparison.OrdinalIgnoreCase);
            return new("generate-request", Literal: input[(start + createPrefix.Length + 1)..]);
        }
        if (string.Equals(normalized, "owned skill", StringComparison.OrdinalIgnoreCase)) { return new("Invalid"); }
        if (!normalized.StartsWith("owned skill ", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SharedSkillSnapshot.MaximumBytes + 512)
        { return new("Invalid"); }
        const string workflowPrefix = "owned skill workflow ";
        if (normalized.StartsWith(workflowPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var start = input.IndexOf(workflowPrefix, StringComparison.OrdinalIgnoreCase);
            return new("workflow", Literal: input[(start + workflowPrefix.Length)..]);
        }
        const string inputPrefix = "owned skill input ";
        if (normalized.StartsWith(inputPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var split = normalized.IndexOf(": ", inputPrefix.Length, StringComparison.Ordinal);
            if (split <= inputPrefix.Length) { return new("Invalid"); }
            var start = input.IndexOf(inputPrefix, StringComparison.OrdinalIgnoreCase);
            return new("input", Literal: input[(start + split + 2)..], InputName: normalized[inputPrefix.Length..split]);
        }
        foreach (var field in new[] { "name", "description", "guidance", "critique", "request", "url", "fixture", "expected-input", "expected-output" })
        {
            var prefix = "owned skill " + field + ": ";
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // Locate the prefix in the original input: prefix routing must not trim the literal payload.
                var start = input.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                return new(field, Literal: input[(start + prefix.Length)..]);
            }
        }
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 3 && !input.Any(char.IsControl))
        {
            var operation = words[2].ToLowerInvariant();
            return new(operation is "editor" or "list" or "validate" or "save" or "cancel" or "enable" or "targets"
                or "next-targets" or "review-tool" or "review-use" or "invoke" or "close-use" or "generate" or "refine" or "cancel-refinement" or "test" or "cancel-test" ? operation : "Invalid");
        }
        if (words.Length == 4 && !input.Any(char.IsControl) && words[2].Equals("tool", StringComparison.OrdinalIgnoreCase)
            && words[3].ToLowerInvariant() is "none" or "web-page-get")
        { return new("tool", Literal: words[3].ToLowerInvariant()); }
        if (words.Length == 4 && !input.Any(char.IsControl) && words[2].Equals("inputs", StringComparison.OrdinalIgnoreCase)
            && words[3].ToLowerInvariant() is "none" or "required-literal")
        { return new("inputs", Literal: words[3].ToLowerInvariant()); }
        if (words.Length == 4 && !input.Any(char.IsControl) && words[2].Equals("target", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(words[3], "D", out var target) && string.Equals(target.ToString("D"), words[3], StringComparison.Ordinal))
        { return new("target", Literal: words[3]); }
        if (words.Length == 5 && !input.Any(char.IsControl) && words[2].ToLowerInvariant() is "create" or "reopen" or "use")
        {
            try { OwnedDeclarativeSkillDocument.RequireIdentity(words[3], words[4]); }
            catch (InvalidDataException) { return new("Invalid"); }
            return new(words[2].ToLowerInvariant(), words[3], words[4]);
        }
        return new("Invalid");
    }
}
