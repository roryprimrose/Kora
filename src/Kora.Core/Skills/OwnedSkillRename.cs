using System.Text;

namespace Kora.Core.Skills;

public sealed class OwnedSkillRename
{
    public OwnedSkillRename(OwnedDeclarativeSkillSnapshot original, string newId)
    {
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
