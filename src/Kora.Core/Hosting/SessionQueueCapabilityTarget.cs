using System.Text.Json.Serialization;

using Kora.Core.Tools;

namespace Kora.Core.Hosting;

/// <summary>One exact current catalogue descriptor ID, never an executable request or arbitrary argument.</summary>
public sealed record SessionQueueCapabilityTarget(string Id)
{
    [JsonIgnore] public CapabilityDescriptor Descriptor => ReadOnlyCapabilityCatalog.FindExact(Id)
        ?? throw new InvalidDataException("The queued descriptor target is not an exact current catalogue ID.");
}
