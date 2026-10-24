using Kora.Core.Hosting;

namespace Kora.Core.Mcp;

/// <summary>Opaque delegated-account metadata. Only a current host broker can resolve its protected credential.</summary>
public sealed record McpAccountReference
{
    public McpAccountReference(Guid accountId, HostRevision revision, Guid secretReference, McpEndpoint destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (accountId == Guid.Empty || secretReference == Guid.Empty || revision.Value <= 0)
        { throw new InvalidDataException("MCP requires an exact revisioned delegated account and protected secret reference."); }
        AccountId = accountId;
        Revision = revision;
        SecretReference = secretReference;
        Destination = destination;
    }

    public Guid AccountId { get; }
    public HostRevision Revision { get; }
    public Guid SecretReference { get; }
    public McpEndpoint Destination { get; }

    public override string ToString() => nameof(McpAccountReference);
}
