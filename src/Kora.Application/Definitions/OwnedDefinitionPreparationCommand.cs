using Kora.Core.Artifacts;
using Kora.Core.Configuration;

namespace Kora.Application.Definitions;

/// <summary>Exact metadata query preparation, never source or execution authority.</summary>
public sealed class OwnedDefinitionPreparationCommand
{
    private OwnedDefinitionPreparationCommand(ArtifactKind? kind = null, string? ownedId = null, bool help = false)
    { Kind = kind; OwnedId = ownedId; IsHelp = help; }

    public const string Syntax = "prepare owned definition <instruction|prompt> <owned-id> | owned definition help. "
        + "Use an exact canonical lowercase owned filename ID, without extension or path. "
        + "Only native metadata kind, ExactOwnedId mode and query are prepared; all other filter drafts remain AND-composed. "
        + "Execute List/Refresh and Inspect separately. Preparation reads nothing and proves no source exists; "
        + "it grants no ownership, trust, save, discard, use or execution authority.";

    public ArtifactKind? Kind { get; }
    public string? OwnedId { get; }
    public bool IsHelp { get; }
    public bool IsValid => IsHelp || OwnedId is not null;
    public override string ToString() => nameof(OwnedDefinitionPreparationCommand);

    public static OwnedDefinitionPreparationCommand? Parse(string input, string assistantName)
    {
        // Prefer a literal command to an ambiguous assistant-name prefix.
        var text = input;
        if (!Reserved(text))
        {
            var name = AssistantNameRules.Normalize(assistantName);
            if (text.StartsWith(name + ", ", StringComparison.OrdinalIgnoreCase))
            { text = text[(name.Length + 2)..]; }
            else if (text.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase))
            { text = text[(name.Length + 1)..]; }
            if (!Reserved(text))
            {
                return text.Contains("owned definition", StringComparison.OrdinalIgnoreCase) ? new() : null;
            }
        }
        var invalid = new OwnedDefinitionPreparationCommand();
        if (input.Length > AssistantNameRules.MaximumLength + 128 || input.Any(char.IsControl)) { return invalid; }
        if (string.Equals(text, "owned definition help", StringComparison.Ordinal)) { return new(help: true); }
        const string instruction = "prepare owned definition instruction ";
        const string prompt = "prepare owned definition prompt ";
        ArtifactKind kind;
        string id;
        if (text.StartsWith(instruction, StringComparison.Ordinal))
        { kind = ArtifactKind.Instruction; id = text[instruction.Length..]; }
        else if (text.StartsWith(prompt, StringComparison.Ordinal))
        { kind = ArtifactKind.Prompt; id = text[prompt.Length..]; }
        else { return invalid; }
        try { OwnedDefinitionInventory.ValidateQuery(id, OwnedDefinitionInventoryQueryMode.ExactOwnedId); }
        catch (InvalidDataException) { return invalid; }
        return new(kind, id);
    }

    private static bool Reserved(string input)
    {
        var words = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2 && (string.Equals(words[0], "prepare", StringComparison.OrdinalIgnoreCase)
            && string.Equals(words[1], "owned", StringComparison.OrdinalIgnoreCase)
            || string.Equals(words[0], "owned", StringComparison.OrdinalIgnoreCase)
            && string.Equals(words[1], "definition", StringComparison.OrdinalIgnoreCase));
    }
}
