using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class ModelOutputSchemaTests
{
    [Theory]
    [InlineData(ModelOutputFormat.DefinitionBodyProposal)]
    [InlineData(ModelOutputFormat.PromptTemplateProposal)]
    public void BodyAndUnsupportedOutcomesHaveExactExclusiveRequiredStringShapes(ModelOutputFormat format)
    {
        var schema = ModelOutputSchemas.GetSchema(format)!.Value;
        if (format == ModelOutputFormat.PromptTemplateProposal)
        {
            schema.EnumerateObject().Select(field => field.Name).Should().Equal("oneOf");
            var alternatives = schema.GetProperty("oneOf");
            alternatives.GetArrayLength().Should().Be(2);
            RequireOnlyString(alternatives[1], "unsupported");
            schema = alternatives[0];
        }
        RequireOnlyString(schema, "body");
    }

    private static void RequireOnlyString(JsonElement schema, string name)
    {
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).Should().Equal(name);
        schema.GetProperty("properties").EnumerateObject().Select(field => field.Name).Should().Equal(name);
        schema.GetProperty("properties").GetProperty(name).GetProperty("type").GetString().Should().Be("string");
    }
}
