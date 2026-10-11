using System.Security.Cryptography;
using System.Text;

using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed class OwnedDefinitionCopy
{
    internal OwnedDefinitionCopy(OwnedInstructionSnapshot source, ArtifactFileDocument draft,
        string destinationName, ArtifactFileDocument proposed)
    {
        Source = source;
        Draft = draft;
        DestinationName = destinationName;
        Proposed = proposed;
        var identity = $"{source.Document.Definition.Id}\n{source.Revision}\n{draft.Definition.Digest}\n{destinationName}\n{proposed.Definition.Digest}";
        AuditTarget = "definition.owned-copy." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public OwnedInstructionSnapshot Source { get; }
    public ArtifactFileDocument Draft { get; }
    public string DestinationName { get; }
    public ArtifactFileDocument Proposed { get; }
    public string AuditTarget { get; }
}
