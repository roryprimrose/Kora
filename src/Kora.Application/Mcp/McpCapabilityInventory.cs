using System.Collections.Immutable;

using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Storage;

namespace Kora.Application.Mcp;

/// <summary>One private, issued, completed discovery snapshot. It confers no read or execution grant.</summary>
public sealed class McpCapabilityInventory
{
    internal McpCapabilityInventory(SessionConversationService owner, HostRequest request, SessionWorkspaceEntry target,
        McpConnectionDescriptor source, ImmutableArray<McpCapability> items, McpCapabilitySelection selection,
        long control, DateTimeOffset expiresAt, McpServerCapabilities capabilities)
    {
        Owner = owner;
        Request = request;
        Target = target;
        Source = source;
        Items = items;
        Selection = selection;
        Control = control;
        ExpiresAt = expiresAt;
        Capabilities = capabilities;
    }

    internal SessionConversationService Owner { get; }
    internal HostRequest Request { get; }
    internal SessionWorkspaceEntry Target { get; }
    internal McpConnectionDescriptor Source { get; }
    internal long Control { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal int Consumed;
    public string ServerId => Source.ServerId;
    public string ServerName => Source.ServerName;
    public ImmutableArray<McpCapability> Items { get; }
    public McpCapabilitySelection Selection { get; }
    public McpServerCapabilities Capabilities { get; }
    public override string ToString() => nameof(McpCapabilityInventory);
}