using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Kora.Core.Authorization;
using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

/// <summary>Native-only review data. Never serialize into model context, ordinary logs or audit.</summary>
public sealed record McpReview(McpReviewStage Stage, McpConnectionIdentity Identity,
    string AccountDisplayName, string? ToolName, string? InputSchema, string? Arguments)
{
    public McpReview(McpReviewStage stage, Uri endpoint, McpAccountReference account, string accountDisplayName,
        string? toolName, string? inputSchema, string? arguments)
        : this(stage, new McpConnectionIdentity(account), accountDisplayName, toolName, inputSchema, arguments)
    {
        if (endpoint != account.Destination.Address) { throw new InvalidDataException("MCP review destination mismatch."); }
    }
    public Uri Endpoint
    {
        get => Identity.Account?.Destination.Address
            ?? throw new InvalidOperationException("A local MCP process has no HTTPS endpoint.");
        init
        {
            var account = Account;
            Identity = new(new McpAccountReference(account.AccountId, account.Revision, account.SecretReference, new(value)));
        }
    }
    public McpAccountReference Account
    {
        get => Identity.Account ?? throw new InvalidOperationException("A local MCP process has no delegated account.");
        init => Identity = new(value);
    }
    public string? ProcessAction { get; init; }
    public string? NativeProcessIdentity { get; init; }
    public McpReviewSource? Source { get; init; }
    public McpSkillReview? Skill { get; init; }
    public McpContinuationReview? Continuation { get; init; }
    public McpCapability? Capability { get; init; }
    public McpContentContinuationReview? ContentContinuation { get; init; }

    internal string InvocationDigest => ProcessAction is not null
        ? Hash(JsonSerializer.Serialize(new { stage = Stage, ProcessAction, NativeProcessIdentity, Identity.Digest }))
        : ContentContinuation is { } content
        ? Hash(JsonSerializer.Serialize(new { stage = Stage, arguments = Arguments, content }))
        : Continuation is { } continuation
        ? Hash(JsonSerializer.Serialize(new { stage = Stage, arguments = Arguments, skill = Skill, continuation }))
        : Skill is { } skill
            ? Hash(JsonSerializer.Serialize(new { stage = Stage, arguments = Arguments, skill }))
            : Hash(JsonSerializer.Serialize(new { stage = Stage, arguments = Arguments }));

    public bool IsBoundTo(HostOperationProposal proposal) =>
        Enum.IsDefined(Stage) && (Stage == McpReviewStage.ResultContinuation) == (Continuation is not null || ContentContinuation is not null)
        && !(Continuation is not null && ContentContinuation is not null)
        && string.Equals(proposal.Binding.ActionId, ProcessAction is null ? "mcp.read-only" : "mcp.process." + ProcessAction, StringComparison.Ordinal)
        && string.Equals(proposal.Binding.SourcePartition, "mcp", StringComparison.Ordinal)
        && (ProcessAction is null ? proposal.Effect == HostOperationEffect.BoundedRead
            : Identity.Stdio is not null && Stage == McpReviewStage.Connection
                && ProcessAction is "launch" or "cleanup" && NativeProcessIdentity is { Length: > 0 }
                && proposal.Effect == HostOperationEffect.BoundedWrite)
        && string.Equals(proposal.Binding.DestinationDigest, Identity.Account?.Destination.Digest ?? Identity.Digest, StringComparison.Ordinal)
        && string.Equals(proposal.Binding.IdentityDigest, Identity.Digest, StringComparison.Ordinal)
        && string.Equals(proposal.Binding.DefinitionDigest, Hash(InputSchema ?? "connection"), StringComparison.Ordinal)
        && string.Equals(proposal.Binding.ResourceDigest, Hash(ToolName ?? "connection"), StringComparison.Ordinal)
        && (Source is { } source
            ? string.Equals(proposal.Binding.DeclaredResourceDigest, Hash(JsonSerializer.Serialize(source)), StringComparison.Ordinal)
                && string.Equals(proposal.Binding.TrackedContentDigest, source.Revision, StringComparison.Ordinal)
            : string.Equals(proposal.Binding.DeclaredResourceDigest, proposal.Binding.TrackedContentDigest, StringComparison.Ordinal))
        && (string.Equals(proposal.Binding.InvocationDigest, InvocationDigest, StringComparison.Ordinal)
            || ProcessAction is null && Continuation is null && ContentContinuation is null && Skill is null
                && string.Equals(proposal.Binding.InvocationDigest, Hash(JsonSerializer.Serialize(new
                { invocation = proposal.Request, stage = Stage, arguments = Arguments })), StringComparison.Ordinal));

    public string NativeText => (Identity.Stdio is null ? "HTTPS" : "Local stdio") + " MCP " + Stage
        + "\nDestination: " + Identity.NativeDestination
        + (NativeProcessIdentity is { } native ? "\nHeld installed binary/directory identity: " + native : string.Empty)
        + (ProcessAction is "launch" ? "\nSEPARATE NATIVE PROCESS LAUNCH CONSENT. Starts this exact installed executable once."
            : ProcessAction is "cleanup" ? "\nSEPARATE OWNED-ROOT CLEANUP CONSENT. Close stdin; after five seconds, permit termination"
                + " of only the still-owned root. No tree kill. Cleanup remains solely for this root after call/source retirement."
                + " Unknown exit or pipe drain retains ownership; root exit does not prove descendant exit."
            : string.Empty)
        + (Source is { } source ? "\nConfigured server: " + source.ServerName + "\nConfiguration source: " + source.Path
            + "\nDependency source ID: " + Kora.Core.Artifacts.ArtifactUseIdentity.McpServerId(source.Path, source.ServerName)
            + "\nExact configuration revision: " + source.Revision : string.Empty)
        + "\n" + (Identity.Stdio is null ? "Delegated account: " : "Credential-free profile: ") + AccountDisplayName
        + "\n" + (ToolName is null ? "Connection and bounded discovery only; no capability is selected yet."
            : Capability is { } capability
                ? "Selected " + capability.Kind + ": " + capability.Name + "\nExact identity: " + capability.Id
                    + "\nComplete discovered definition:\n" + InputSchema
                : "Read-only mapped tool: " + ToolName + "\nExact input schema:\n" + InputSchema)
        + (InputSchema is null ? string.Empty : "\nSchema SHA-256: " + Hash(InputSchema))
        + (Arguments is null ? string.Empty : "\nExact outgoing arguments:\n" + Arguments)
        + (Skill is { } skill ? "\nReviewed declarative skill: " + skill.Identity + "\nExact skill revision: " + skill.Revision
            + "\nOriginal request:\n" + skill.OriginalRequest + "\nOriginal arguments SHA-256: " + skill.ArgumentsDigest
            + "\nOne local skill continuation only; the skill and result are untrusted evidence, not authority." : string.Empty)
        + (Continuation is { } continuation
            ? "\nSEPARATE ONE-USE LOCAL MODEL CONTINUATION CONSENT"
                + "\nOriginal Active conversation: " + continuation.ConversationName
                + "\nOriginal channel: " + continuation.Origin
                + "\nUnchanged original task:\n" + continuation.Question
                + "\nObserved UTC: " + continuation.Result.ObservedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                + "\nCOMPLETE UNCHANGED TEXT\n" + continuation.Result.Text
                + "\nCOMPLETE UNCHANGED STRUCTURED JSON\n" + (continuation.Result.StructuredJson ?? "(none)")
                + "\n" + Kora.Core.Context.LocalSourceRiskPolicy.Disclosure
                + "\nConsent permits only the same bounded original local model task to continue with this exact inert result."
                + " It grants no further MCP call, refresh, hosted transfer, memory write or export."
                + " Retain only the original task and genuine answer or truthful HOST terminal in this conversation;"
                + " no extra durable raw-result copy. Decline or cancellation does not roll back the completed external read."
            : string.Empty)
        + (ContentContinuation?.NativeText ?? string.Empty)
        + (Identity.Stdio is null
            ? "\nExternal processing destination. No redirect, remote mutation, other tool, ambient credentials or hosted-model disclosure is approved."
            : "\nThis installed process is not contained. Launch authority does not classify its tools or authorize hosted-model disclosure.");

    public override string ToString() => nameof(McpReview);

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}