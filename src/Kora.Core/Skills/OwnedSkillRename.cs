using System.Text;

namespace Kora.Core.Skills;

public sealed class OwnedSkillRename
{
    public OwnedSkillRename(OwnedDeclarativeSkillSnapshot original, string newId)
    {
        if (original.Document.ReferenceDeclaration is not null)
        { throw new InvalidDataException("Rename supports only complete native v1/v2/v3/v4 two-file sources, never Markdown reference packages."); }
        OwnedDeclarativeSkillDocument.RequireIdentity(newId, original.Document.Version);
        if (string.Equals(original.Document.Id, newId, StringComparison.Ordinal))
        { throw new InvalidDataException("Choose a distinct canonical skill ID. Rename does not change the version or readable name."); }
        Original = original;
        var metadata = original.Document.ManifestText.Split('\n');
        var index = Array.FindIndex(metadata, line => line.StartsWith("id: ", StringComparison.Ordinal));
        metadata[index] = "id: " + newId;
        Renamed = OwnedDeclarativeSkillDocument.Parse(Encoding.UTF8.GetBytes(string.Join('\n', metadata)),
            original.Document.InstructionBytes.Span);
    }

    public OwnedDeclarativeSkillSnapshot Original { get; }
    public OwnedDeclarativeSkillDocument Renamed { get; }
    public override string ToString() => $"{Original.Document.Id} {Original.Document.Version} -> {Renamed.Id} (recovery required)";
}
