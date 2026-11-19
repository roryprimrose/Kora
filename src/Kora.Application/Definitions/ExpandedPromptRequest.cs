using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

/// <summary>Frozen user data; the expansion digest is never substituted for the original source digest.</summary>
public sealed class ExpandedPromptRequest
{
    internal ExpandedPromptRequest(ArtifactDefinition source, Guid preparation, long valueRevision, string userRequest)
    {
        Source = source;
        Preparation = preparation;
        ValueRevision = valueRevision;
        UserRequest = userRequest;
        Digest = OwnedPromptTemplate.RequestDigest(userRequest);
    }

    public ArtifactDefinition Source { get; }
    public Guid Preparation { get; }
    public long ValueRevision { get; }
    public string UserRequest { get; }
    public string Digest { get; }

}
