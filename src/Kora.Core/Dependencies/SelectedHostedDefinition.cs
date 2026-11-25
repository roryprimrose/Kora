using System.Text;

using Kora.Core.Customizations;

namespace Kora.Core.Dependencies;

/// <summary>Exact lower-trust Claude USER data, not read, retention, transmission or execution authority.</summary>
public sealed class SelectedHostedDefinition
{
    public SelectedHostedDefinition(CustomizationKind kind, string sourceId, string revision, string name,
        string description, string sourceText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);
        if (kind is not (CustomizationKind.Skill or CustomizationKind.Agent)
            || sourceId.Length > 1024 || revision.Length > 256 || name.Length > 64
            || !char.IsAsciiLetterOrDigit(name[0])
            || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))
            || description.Length > 1024 || description.Any(char.IsControl) || sourceText[0] == '\ufeff')
        { throw new InvalidDataException("One bounded exact skill or v1 agent copy is required."); }
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(sourceText) > 32768)
            { throw new InvalidDataException("The exact selected source exceeds its UTF-8 bound."); }
        }
        catch (EncoderFallbackException exception)
        { throw new InvalidDataException("The exact selected source is not valid UTF-8 text.", exception); }
        Kind = kind; SourceId = sourceId; Revision = revision; Name = name;
        Description = description; SourceText = sourceText;
    }

    public CustomizationKind Kind { get; }
    public string SourceId { get; }
    public string Revision { get; }
    public string Name { get; }
    public string Description { get; }
    public string SourceText { get; }
}
