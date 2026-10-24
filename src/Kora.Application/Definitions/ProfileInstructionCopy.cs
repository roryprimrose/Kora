using System.Security.Cryptography;
using System.Text;

using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed class ProfileInstructionCopy
{
    internal ProfileInstructionCopy(ProfileInstructionSnapshot source, string destinationName, ArtifactFileDocument proposed)
    {
        Source = source;
        DestinationName = destinationName;
        Proposed = proposed;
        AuditTarget = "definition.profile-copy." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{source.Identity}\n{destinationName}\n{proposed.Definition.Digest}")));
    }

    public ProfileInstructionSnapshot Source { get; }
    public string DestinationName { get; }
    public ArtifactFileDocument Proposed { get; }
    public string AuditTarget { get; }
}
