using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.Core.Mcp;

namespace Kora.Core.Dependencies;

/// <summary>Immutable, session-bound data; none of its text carries approval, destination or memory authority.</summary>
public sealed class ModelContextEnvelope
{
    public const int MaximumInputUtf8Bytes = 32768;
    public const int MaximumEvidenceItems = 16;
    public static int MaximumOutputUtf8Bytes => ReadOnlyCapabilityCatalog.Limits.MaximumOutputUtf8Bytes;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public ModelContextEnvelope(HostRequest request, string systemPolicy, string userRequest,
        IEnumerable<ModelContextEvidence> evidence, DateTimeOffset createdAt, DateTimeOffset expiresAt,
        ModelToolMode toolMode = ModelToolMode.ReadOnly, McpModelTool? mcpTool = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(systemPolicy);
        ArgumentNullException.ThrowIfNull(userRequest);
        ArgumentNullException.ThrowIfNull(evidence);
        _ = ModelToolProtocol.GetTools(toolMode);
        if ((toolMode == ModelToolMode.McpReadOnly) != (mcpTool is not null))
        { throw new InvalidDataException("An MCP turn requires exactly its reviewed request-owned tool."); }
        if (expiresAt <= createdAt)
        {
            throw new InvalidDataException("A context envelope requires a finite positive validity interval.");
        }
        Request = request;
        SystemPolicy = systemPolicy;
        UserRequest = userRequest;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        ToolMode = toolMode;
        McpTool = mcpTool;
        Evidence = ValidateContent(request, systemPolicy, userRequest, evidence);
    }

    internal static ImmutableArray<ModelContextEvidence> ValidateContent(HostRequest request, string systemPolicy,
        string userRequest, IEnumerable<ModelContextEvidence> evidence)
    {
        var items = evidence.Take(MaximumEvidenceItems + 1).ToImmutableArray();
        if (items.Length > MaximumEvidenceItems)
        {
            throw new InvalidDataException("The context evidence count exceeds the host limit.");
        }
        ValidateText(systemPolicy);
        ValidateText(userRequest);
        var ids = new HashSet<HostId<EvidenceIdentity>>();
        foreach (var item in items)
        {
            if (item is null) { throw new InvalidDataException("A context evidence item is missing."); }
            item.Id.Validate();
            if (!ids.Add(item.Id) || item.Request != request || item.Revision.Value <= 0
                || item.Disclosure is not (ModelEvidenceDisclosure.LocalOnly or ModelEvidenceDisclosure.HostedEligible))
            {
                throw new InvalidDataException("Evidence must have unique, current, exact-request host provenance.");
            }
            ValidateText(item.Content);
        }
        return items;
    }

    public HostRequest Request { get; }
    public string SystemPolicy { get; }
    public string UserRequest { get; }
    public ImmutableArray<ModelContextEvidence> Evidence { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public ModelToolMode ToolMode { get; }
    public McpModelTool? McpTool { get; }

    public byte[] Serialize() => McpTool is { } external ? JsonSerializer.SerializeToUtf8Bytes(new
    {
        SystemPolicy,
        UserRequest,
        Evidence,
        ToolMode,
        Tools = ModelToolProtocol.GetTools(ToolMode),
        MaximumOutputUtf8Bytes,
        McpTool = external,
    }) : JsonSerializer.SerializeToUtf8Bytes(new
    {
        SystemPolicy,
        UserRequest,
        Evidence,
        ToolMode,
        Tools = ModelToolProtocol.GetTools(ToolMode),
        MaximumOutputUtf8Bytes,
    });

    public void RequireCompleteInputLimit()
    {
        if (Serialize().Length > MaximumInputUtf8Bytes)
        { throw new InvalidDataException("The complete serialized context exceeds 32768 UTF-8 bytes; nothing was dropped."); }
    }

    private static void ValidateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (StrictUtf8.GetByteCount(text) > MaximumInputUtf8Bytes)
        {
            throw new InvalidDataException("Context text exceeds the complete-envelope limit.");
        }
    }
}
