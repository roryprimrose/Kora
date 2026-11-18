using System.Text;

namespace Kora.Core.Dependencies;

/// <summary>One immutable reviewed hosted copy; never a source-read, retention or transmission grant.</summary>
public sealed class CopilotCustomizationSelection
{
    public CopilotCustomizationSelection(bool skill, string sourceId, string revision, string name,
        string description, string sourceText, string instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        if (sourceId.Length > 1024 || revision.Length > 256 || name.Length > 64
            || !char.IsAsciiLetterOrDigit(name[0])
            || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))
            || description.Length > 1024 || description.Any(char.IsControl)
            || new UTF8Encoding(false, true).GetByteCount(sourceText) > 32768
            || !sourceText.Contains(instructions, StringComparison.Ordinal))
        { throw new InvalidDataException("A single bounded exact native customization copy is required."); }
        IsSkill = skill;
        SourceId = sourceId;
        Revision = revision;
        Name = name;
        Description = description;
        SourceText = sourceText;
        Instructions = instructions;
    }

    public bool IsSkill { get; }
    public string SourceId { get; }
    public string Revision { get; }
    public string Name { get; }
    public string Description { get; }
    public string SourceText { get; }
    public string Instructions { get; }
}
