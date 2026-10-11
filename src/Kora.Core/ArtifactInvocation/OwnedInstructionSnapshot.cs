namespace Kora.Core.Artifacts;

public sealed record OwnedInstructionSnapshot(
    string Name, string DirectoryIdentity, string FileIdentity, ArtifactFileDocument Document)
{
    public string Revision => Document.Definition.Digest + "." + DirectoryIdentity + "." + FileIdentity;
}
