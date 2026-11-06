using System.Text;
using System.Text.Json;

namespace Kora.Core.Tools;

/// <summary>Exact portable input schemas for the original six observation capabilities.</summary>
public static class ReadOnlyCapabilitySchema
{
    public static string Serialize(string id)
    {
        var descriptor = ReadOnlyCapabilityCatalog.FindExact(id)
            ?? throw new InvalidDataException("Only original observation capabilities have this schema.");
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            CapabilityInputSchema.Write(writer, descriptor, includeEmptyRequired: true, boundedOffset: true);
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }

}
