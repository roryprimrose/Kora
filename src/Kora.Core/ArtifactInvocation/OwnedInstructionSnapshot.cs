using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Artifacts;

public sealed record OwnedInstructionSnapshot(
    string Name, string DirectoryIdentity, string FileIdentity, ArtifactFileDocument Document)
{
    public string CanonicalFileName { get; init; } = Name + OwnedDefinitionKind.Extension(Document.Definition.Kind);
    public string RootBindingIdentity { get; init; } = string.Empty;
    public string Revision => Document.Definition.Digest + "." + DirectoryIdentity + "." + FileIdentity
        + "." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalFileName)));
}
