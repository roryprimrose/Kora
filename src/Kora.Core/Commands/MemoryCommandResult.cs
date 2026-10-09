using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Memory;

namespace Kora.Core.Commands;

public sealed record MemoryCommandResult(string Outcome, string Message)
{
    public ImmutableArray<MemorySummary> Memories { get; init; } = [];
    // Only an explicit exact inspection returns a candidate, lineage and receipt.
    public MemoryRecord? Inspected { get; init; }
    public static byte[] Serialize(MemoryCommandResult result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
        if (bytes.Length > SessionCommand.MaximumResultBytes)
        {
            throw new InvalidDataException("Memory management result exceeds 64 KiB.");
        }
        return bytes;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<MemoryScopeKind>(), new JsonStringEnumConverter<MemoryReviewState>(),
            new JsonStringEnumConverter<MemoryRetentionState>(), new JsonStringEnumConverter<MemoryContentClass>(),
            new JsonStringEnumConverter<MemoryProposalOrigin>() },
    };
}
