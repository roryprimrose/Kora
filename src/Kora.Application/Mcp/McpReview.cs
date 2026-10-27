using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kora.Core.Authorization;
using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

/// <summary>Native-only review data. Never serialize into model context, ordinary logs or audit.</summary>
public sealed record McpReview(McpReviewStage Stage, Uri Endpoint, McpAccountReference Account,
    string AccountDisplayName, string? ToolName, string? InputSchema, string? Arguments)
{
    public McpReviewSource? Source { get; init; }

    public bool IsBoundTo(HostOperationProposal proposal) =>
        Enum.IsDefined(Stage) && string.Equals(proposal.Binding.ActionId, "mcp.read-only", StringComparison.Ordinal)
        && string.Equals(proposal.Binding.SourcePartition, "mcp", StringComparison.Ordinal)
        && proposal.Effect == HostOperationEffect.BoundedRead && Account.Destination == new McpEndpoint(Endpoint)
        && string.Equals(proposal.Binding.DestinationDigest, Account.Destination.Digest, StringComparison.Ordinal)
        && string.Equals(proposal.Binding.IdentityDigest, Hash(JsonSerializer.Serialize(Account)), StringComparison.Ordinal)
        && string.Equals(proposal.Binding.DefinitionDigest, Hash(InputSchema ?? "connection"), StringComparison.Ordinal)
        && string.Equals(proposal.Binding.ResourceDigest, Hash(ToolName ?? "connection"), StringComparison.Ordinal)
        && (Source is { } source
            ? string.Equals(proposal.Binding.DeclaredResourceDigest, Hash(JsonSerializer.Serialize(source)), StringComparison.Ordinal)
                && string.Equals(proposal.Binding.TrackedContentDigest, source.Revision, StringComparison.Ordinal)
            : string.Equals(proposal.Binding.DeclaredResourceDigest, proposal.Binding.TrackedContentDigest, StringComparison.Ordinal))
        && string.Equals(proposal.Binding.InvocationDigest, Hash(JsonSerializer.Serialize(new
            { invocation = proposal.Request, stage = Stage, arguments = Arguments })), StringComparison.Ordinal);

    public string NativeText => "HTTPS MCP " + Stage + "\nDestination: " + Endpoint.AbsoluteUri
        + (Source is { } source ? "\nConfigured server: " + source.ServerName + "\nConfiguration source: " + source.Path : string.Empty)
        + "\nDelegated account: " + AccountDisplayName
        + "\n" + (ToolName is null ? "Connection and bounded discovery only; no tool is selected yet."
            : "Read-only mapped tool: " + ToolName + "\nExact input schema:\n" + InputSchema)
        + (Arguments is null ? string.Empty : "\nExact outgoing arguments:\n" + Arguments)
        + "\nExternal processing destination. No redirect, remote mutation, other tool, ambient credentials or hosted-model disclosure is approved.";

    public override string ToString() => nameof(McpReview);

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
