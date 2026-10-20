using Kora.Core.Artifacts;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

/// <summary>Prepares retained correlation metadata, never task or session authority.</summary>
public sealed class AllWorkTaskQueryCommand
{
    private AllWorkTaskQueryCommand(bool help, Guid? taskId, bool valid, bool activated)
    {
        IsHelp = help;
        TaskId = taskId;
        IsValid = valid;
        IsActivated = activated;
    }

    public const string PreparePrefix = "prepare all work task ";
    public const string HelpPhrase = "all work query help";
    public const string Guidance = "Use prepare all work task <task-id> with an exact nonempty lowercase D GUID, "
        + "or all work query help. Preparation only opens/focuses the existing All Work task-ID field; it reads nothing. "
        + "Request ID and ALL other filter drafts remain AND-composed; IDs and constrained state must match the same validated retained task. "
        + "Review every visible predicate, then explicitly Refresh for a bounded retained overview. "
        + "Missing, unavailable or omitted tasks prove no absence or exhaustive search. "
        + "Correlation is future-query metadata, not current identity, selection, ownership, permission, approval or action authority.";
    public static IReadOnlyList<string> DiscoveryPhrases { get; } =
        Array.AsReadOnly(new[] { "prepare all work task", HelpPhrase });

    public bool IsHelp { get; }
    public Guid? TaskId { get; }
    public bool IsValid { get; }
    public bool IsActivated { get; }
    public override string ToString() => nameof(AllWorkTaskQueryCommand);

    public static AllWorkTaskQueryCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = input.TrimStart(' ');
        var activated = false;
        // Reserve the grammar before stripping a name such as "Prepare" or "All Work".
        if (!IsReserved(text))
        {
            var withoutActivation = ArtifactCommandRouter.RemoveActivationPrefix(text, assistantName);
            activated = !string.Equals(text, withoutActivation, StringComparison.Ordinal);
            text = withoutActivation;
        }
        if (!IsReserved(text)) { return null; }
        if (input.Length > 128 || input.Any(char.IsControl))
        {
            return new(false, null, false, activated);
        }
        if (string.Equals(text, HelpPhrase, StringComparison.OrdinalIgnoreCase))
        {
            return new(true, null, true, activated);
        }
        if (text.StartsWith(PreparePrefix, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var id = OptionalCanonicalGuid.Parse(text[PreparePrefix.Length..]);
                if (id is not null) { return new(false, id, true, activated); }
            }
            catch (InvalidDataException) { }
        }
        return new(false, null, false, activated);
    }

    private static bool IsReserved(string text)
    {
        var normalized = BuiltInCommandRouter.Normalize(text);
        return normalized.StartsWith("prepare all work", StringComparison.Ordinal)
            || normalized.StartsWith("all work query", StringComparison.Ordinal);
    }
}
