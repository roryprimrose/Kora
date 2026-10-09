using System.Globalization;
using System.Text;
using Kora.Core.Configuration;
using Kora.Core.Memory;

namespace Kora.Core.Commands;

/// <summary>Exact session/identity/revision selectors; quoted values are content, never authority.</summary>
public sealed record MemoryCommand(MemoryCommandOperation Operation, Guid? SessionId = null,
    Guid? MemoryId = null, long Revision = 0, bool Accept = false,
    MemoryCandidate? Candidate = null, string? Error = null)
{
    public const int MaximumInputBytes = MemoryPolicy.MaximumCandidateUtf8Bytes + 512;
    public static IReadOnlyList<string> DiscoveryPhrases { get; } = ["memory help", "list memories"];
    public const string Syntax = "memory help | memory list <session-id> | memory inspect/get <session-id> <memory-id> <revision>"
        + " | memory review <session-id> <memory-id> <revision> accept|reject"
        + " | memory admit/disable/forget <session-id> <memory-id> <revision>"
        + " | memory edit/set <session-id> <memory-id> <revision> ExplicitFact|ResponsePreference|WorkflowPreference|Decision \"<exact value>\"."
        + " Canonical nonempty D GUIDs and positive decimal revisions only. Double a quote inside the final quoted value."
        + " Session only; list is content-free. Inspect before review; review does not admit."
        + " Edit clears review and returns to Proposed/Pending. No proposal trigger, recall, model tool or hosted disclosure.";

    public static MemoryCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (text.Equals("list memories", StringComparison.OrdinalIgnoreCase)) { return new(MemoryCommandOperation.Help); }
        if (!text.Equals("memory", StringComparison.OrdinalIgnoreCase)
            && !(text.StartsWith("memory", StringComparison.OrdinalIgnoreCase) && char.IsWhiteSpace(text[6]))) { return null; }
        static MemoryCommand Invalid() => new(MemoryCommandOperation.Invalid, Error: Syntax);
        if (input.Any(char.IsControl)) { return Invalid(); }
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(input) > MaximumInputBytes) { return Invalid(); }
        }
        catch (EncoderFallbackException) { return Invalid(); }
        string? value = null;
        var quote = text.IndexOf('"', StringComparison.Ordinal);
        if (quote >= 0)
        {
            if (text[quote - 1] != ' ') { return Invalid(); }
            var builder = new StringBuilder();
            var closed = false;
            for (var index = quote + 1; index < text.Length; index++)
            {
                if (text[index] != '"') { builder.Append(text[index]); continue; }
                if (index + 1 < text.Length && text[index + 1] == '"') { builder.Append('"'); index++; continue; }
                closed = index == text.Length - 1;
                break;
            }
            if (!closed) { return Invalid(); }
            value = builder.ToString();
            text = text[..quote].TrimEnd();
        }
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 2 && words[1].Equals("help", StringComparison.OrdinalIgnoreCase) && value is null)
        {
            return new(MemoryCommandOperation.Help);
        }
        if (words.Length < 3 || !ExactId(words[2], out var session)) { return Invalid(); }
        var verb = words[1].ToLowerInvariant();
        if (verb is "list" && words.Length == 3 && value is null) { return new(MemoryCommandOperation.List, session); }
        if (words.Length < 5 || !ExactId(words[3], out var memory)
            || !long.TryParse(words[4], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision <= 0)
        {
            return Invalid();
        }
        if (words.Length == 5 && value is null)
        {
            var operation = verb switch
            {
                "inspect" or "get" => MemoryCommandOperation.Inspect,
                "admit" => MemoryCommandOperation.Admit,
                "disable" => MemoryCommandOperation.Disable,
                "forget" => MemoryCommandOperation.Forget,
                _ => MemoryCommandOperation.Invalid,
            };
            return operation == MemoryCommandOperation.Invalid ? Invalid() : new(operation, session, memory, revision);
        }
        if (verb is "review" && words.Length == 6 && value is null
            && words[5].ToLowerInvariant() is "accept" or "reject")
        {
            return new(MemoryCommandOperation.Review, session, memory, revision,
                words[5].Equals("accept", StringComparison.OrdinalIgnoreCase));
        }
        if (verb is "edit" or "set" && words.Length == 6 && value is not null)
        {
            var contentClass = words[5].ToLowerInvariant() switch
            {
                "explicitfact" => MemoryContentClass.ExplicitFact,
                "responsepreference" => MemoryContentClass.ResponsePreference,
                "workflowpreference" => MemoryContentClass.WorkflowPreference,
                "decision" => MemoryContentClass.Decision,
                _ => MemoryContentClass.Unknown,
            };
            var candidate = new MemoryCandidate(contentClass, value);
            if (MemoryPolicy.ValidateCandidate(candidate) == MemoryReason.None)
            {
                return new(MemoryCommandOperation.Edit, session, memory, revision, Candidate: candidate);
            }
        }
        return Invalid();
    }

    private static bool ExactId(string value, out Guid id) =>
        Guid.TryParseExact(value, "D", out id) && id != Guid.Empty
        && value.Equals(id.ToString("D"), StringComparison.OrdinalIgnoreCase);
}
