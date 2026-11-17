using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Theory]
    [InlineData(ModelOutputFormat.DefinitionBodyProposal, false)]
    [InlineData(ModelOutputFormat.PromptTemplateProposal, false)]
    [InlineData(ModelOutputFormat.PromptTemplateProposal, true)]
    public async Task DefinitionAuthoringProjectsExactNativeSchemaAndPreservesCompleteAnswer(ModelOutputFormat format, bool unsupported)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        const string literal = "  Literal e\u0301\U0001f642\r\n${input:topic}\n";
        var answer = unsupported ? JsonSerializer.Serialize(new { unsupported = literal }) : JsonSerializer.Serialize(new { body = literal });
        f.Chat = (message, _) => Task.FromResult(Json(message, JsonSerializer.Serialize(new
        {
            model = Model, done = true, message = new { role = "assistant", content = answer },
        })));
        var reply = await f.Transport.CompleteAsync(f.Request(toolMode: ModelToolMode.None, outputFormat: format), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        reply.Message.Should().BeOfType<ModelFinalResponse>().Which.Answer.Should().Be(answer);
        using var sent = JsonDocument.Parse(f.Bodies.Should().ContainSingle().Subject);
        var physicalSchema = sent.RootElement.GetProperty("format");
        JsonElement.DeepEquals(physicalSchema, ModelOutputSchemas.GetSchema(format)!.Value).Should().BeTrue();
        if (format == ModelOutputFormat.PromptTemplateProposal)
        {
            var alternatives = physicalSchema.GetProperty("oneOf");
            alternatives.GetArrayLength().Should().Be(2);
            alternatives[1].GetProperty("required")[0].GetString().Should().Be("unsupported");
            alternatives[1].GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
            physicalSchema = alternatives[0];
        }
        physicalSchema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).Should().Equal("body");
        physicalSchema.GetProperty("properties").EnumerateObject().Select(field => field.Name).Should().Equal("body");
        physicalSchema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        physicalSchema.GetProperty("properties").GetProperty("body").GetProperty("type").GetString().Should().Be("string");
        sent.RootElement.TryGetProperty("tools", out _).Should().BeFalse();
        sent.RootElement.GetProperty("stream").GetBoolean().Should().BeFalse();
        sent.RootElement.GetProperty("think").GetBoolean().Should().BeFalse();
        sent.RootElement.GetProperty("truncate").GetBoolean().Should().BeFalse();
        sent.RootElement.GetProperty("shift").GetBoolean().Should().BeFalse();
        reply.Usage!.InputUtf8Bytes.Should().BeGreaterThanOrEqualTo(f.Bodies[0].Length);
    }

    [Theory]
    [InlineData(ModelOutputFormat.DefinitionBodyProposal)]
    [InlineData(ModelOutputFormat.PromptTemplateProposal)]
    public async Task StructuredAuthoringRejectsUnsolicitedNativeToolsWithoutContinuation(ModelOutputFormat format)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Chat = (message, _) => Task.FromResult(Json(message, Call));
        var reply = await f.Transport.CompleteAsync(f.Request(toolMode: ModelToolMode.None, outputFormat: format), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Message.Should().BeNull();
        f.Bodies.Should().ContainSingle();
    }
}
