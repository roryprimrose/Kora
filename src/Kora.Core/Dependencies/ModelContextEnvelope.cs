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
        ModelToolMode toolMode = ModelToolMode.ReadOnly, ModelTurnConstraints? constraints = null,
        McpModelTool? mcpTool = null, Network.LocalWebPageScope? webPages = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(systemPolicy);
        ArgumentNullException.ThrowIfNull(userRequest);
        ArgumentNullException.ThrowIfNull(evidence);
        _ = ModelToolProtocol.GetTools(toolMode);
        _ = constraints?.GetTools(toolMode);
        if (webPages is not null && toolMode != ModelToolMode.LocalWebPage)
        { throw new InvalidDataException("Explicit page destinations require the local web-page tool mode."); }
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
        Constraints = constraints;
        McpTool = mcpTool;
        WebPages = webPages;
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
            if (item.HistorySource is { } history)
            {
                history.Validate();
                if (history.Revision != item.Revision.Value || item.Disclosure != ModelEvidenceDisclosure.LocalOnly)
                { throw new InvalidDataException("Host history lineage must bind the exact local-only evidence revision."); }
            }
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
    public ModelTurnConstraints? Constraints { get; }
    public IReadOnlyList<CapabilityDescriptor> Tools => Constraints?.GetTools(ToolMode) ?? ModelToolProtocol.GetTools(ToolMode);
    public McpModelTool? McpTool { get; }
    public Network.LocalWebPageScope? WebPages { get; }

    public byte[] Serialize()
    {
        if (WebPages is not null)
        {
            return JsonSerializer.SerializeToUtf8Bytes(new
            { SystemPolicy, UserRequest, Evidence, ToolMode, Tools, MaximumOutputUtf8Bytes, WebPages });
        }
        if (McpTool is not null)
        {
            return JsonSerializer.SerializeToUtf8Bytes(new
            { SystemPolicy, UserRequest, Evidence, ToolMode, Tools, MaximumOutputUtf8Bytes, McpTool });
        }
        if (Constraints is null)
        {
            return JsonSerializer.SerializeToUtf8Bytes(new
            { SystemPolicy, UserRequest, Evidence, ToolMode, Tools, MaximumOutputUtf8Bytes });
        }
        return JsonSerializer.SerializeToUtf8Bytes(new
        { SystemPolicy, UserRequest, Evidence, ToolMode, Tools, MaximumOutputUtf8Bytes, Constraints });
    }

    public void RequireCompleteInputLimit() => RequireCompleteInputLimit(0);

    public void RequireCompleteInputLimit(int reservedInputUtf8Bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reservedInputUtf8Bytes);
        if ((long)Serialize().Length + reservedInputUtf8Bytes > (Constraints?.InputUtf8Bytes ?? MaximumInputUtf8Bytes))
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
