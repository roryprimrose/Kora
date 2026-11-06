using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Tools;
using Kora.Core.Mcp;

namespace Kora.Core.UnitTests.Tools;

public sealed class CapabilityInputSchemaTests
{
    [Theory]
    [InlineData(ProviderSettingsTools.Get, "{}")]
    [InlineData(ProviderSettingsTools.Models, """{"service":"Claude","offset":0,"count":6}""")]
    [InlineData(ProviderSettingsTools.Model, """{"service":"Local","model":"exact-model"}""")]
    [InlineData(ProviderSettingsTools.Refresh, """{"service":"Copilot"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Unset"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","clearModel":true}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","model":"exact","reasoning":"effort","context":"window","account":"account","revision":1}""")]
    public void Registered_provider_settings_schemas_preserve_the_existing_exact_inputs(string id, string input)
    {
        var descriptor = KoraMcpCatalog.Descriptors.Single(tool => string.Equals(tool.Id, id, StringComparison.Ordinal));
        KoraMcpCatalog.ToolIds.Should().Contain(id);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            CapabilityInputSchema.Write(writer, descriptor, includeEmptyRequired: true, boundedOffset: true);
        }
        using var schema = JsonDocument.Parse(stream.ToArray());
        var root = schema.RootElement;
        root.GetProperty("type").GetString().Should().Be("object");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        var properties = root.GetProperty("properties");
        var expected = id switch
        {
            ProviderSettingsTools.Get => Array.Empty<string>(),
            ProviderSettingsTools.Models => ["service", "offset", "count"],
            ProviderSettingsTools.Model => ["service", "model"],
            ProviderSettingsTools.Refresh => ["service"],
            _ => ["service", "model", "reasoning", "context", "account", "revision", "clearModel"],
        };
        properties.EnumerateObject().Select(field => field.Name).Should().Equal(expected);
        root.GetProperty("required").EnumerateArray().Select(field => field.GetString()).Should().Equal(
            id is ProviderSettingsTools.Get ? [] : id is ProviderSettingsTools.Model ? ["service", "model"] : ["service"]);
        using var arguments = JsonDocument.Parse(input);
        foreach (var argument in arguments.RootElement.EnumerateObject())
        {
            var field = properties.GetProperty(argument.Name);
            if (string.Equals(argument.Name, "service", StringComparison.Ordinal))
            { field.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).Should().Contain(argument.Value.GetString()); }
            else
            {
                field.GetProperty("type").GetString().Should().Be(argument.Value.ValueKind switch
                {
                    JsonValueKind.String => "string",
                    JsonValueKind.Number => "integer",
                    _ => "boolean",
                });
            }
        }
        CapabilityInputPolicy.Read(descriptor, input).DenialReason.Should().BeNull();
    }

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
