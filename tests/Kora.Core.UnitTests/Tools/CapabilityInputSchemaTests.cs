using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Tools;

public sealed class CapabilityInputSchemaTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Provider_and_private_schema_framing_keeps_explicit_required_and_offset_bounds(bool required, bool bounded)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            CapabilityInputSchema.Write(writer, ReadOnlyCapabilityCatalog.FindExact(ReadOnlyCapabilityCatalog.List)!,
                required, bounded);
        }
        using var schema = JsonDocument.Parse(stream.ToArray());
        var root = schema.RootElement;
        root.TryGetProperty("required", out var fields).Should().Be(required);
        if (required) { fields.GetArrayLength().Should().Be(0); }
        var offset = root.GetProperty("properties").GetProperty("offset");
        offset.TryGetProperty("maximum", out var maximum).Should().Be(bounded);
        if (bounded) { maximum.GetInt32().Should().Be(int.MaxValue); }
        Encoding.UTF8.GetString(stream.ToArray()).Should().Contain("\"maximum\":6");
    }
}
