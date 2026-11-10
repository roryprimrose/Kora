using Kora.Core.Mcp;
using Kora.Core.Skills;

namespace Kora.Application.Mcp;

/// <summary>Trusted host registration only. Discovery/configuration/annotations cannot populate this catalogue.</summary>
public sealed class McpReadOnlyPolicy : IDisposable
{
    private readonly McpReadOnlyMapping[] mappings;
    private readonly Lock gate = new();
    private DescriptorAdmission? descriptor;
    private bool disposed;

    public McpReadOnlyPolicy(IEnumerable<McpReadOnlyMapping> mappings) => this.mappings = mappings.ToArray();

    internal McpReadOnlyMapping? Find(McpConnectionDescriptor source, McpDiscoveredTool tool) =>
        mappings.SingleOrDefault(mapping => mapping.Endpoint == source.Endpoint && mapping.Account == source.Account
            && string.Equals(mapping.SourceRevision, source.SourceRevision, StringComparison.Ordinal)
            && string.Equals(mapping.ToolName, tool.Name, StringComparison.Ordinal)
            && string.Equals(mapping.SchemaDigest, tool.Schema.Digest, StringComparison.Ordinal));

    internal DescriptorAdmission ObservePreparedDescriptor(McpConnectionDescriptor source, McpDiscoveredTool tool,
        Func<bool> current, TimeProvider time, DateTimeOffset expires)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Find(source, tool) is null || !current())
            { throw new InvalidOperationException("No exact trusted read-only descriptor admission is available."); }
            descriptor?.Retire();
            descriptor = new(new(source.ServerId, source.SourceRevision, tool.Name, tool.Schema.Digest),
            current, time, expires);
            return descriptor;
        }
    }

    internal DescriptorAdmission CaptureGuidanceDescriptor(DeclarativeMcpToolReference reference)
    {
        lock (gate)
        {
            if (disposed || descriptor is null || descriptor.Reference != reference || !descriptor.IsCurrent())
            { throw new InvalidOperationException("Read-only MCP guidance refinement requires an exact still-held trusted runtime descriptor. Nothing connected or sent."); }
            return descriptor;
        }
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; descriptor?.Retire(); descriptor = null; }
    }

    internal sealed class DescriptorAdmission(DeclarativeMcpToolReference reference,
        Func<bool> current, TimeProvider time, DateTimeOffset expires)
    {
        private readonly DateTimeOffset observed = time.GetUtcNow();
        private int retired;
        internal DeclarativeMcpToolReference Reference { get; } = reference;
        internal void Retire() => Interlocked.Exchange(ref retired, 1);
        internal bool IsCurrent()
        {
            if (Volatile.Read(ref retired) != 0) { return false; }
            var now = time.GetUtcNow();
            if (!current() || now < observed || now >= expires) { Retire(); return false; }
            return Volatile.Read(ref retired) == 0;
        }
    }
}
