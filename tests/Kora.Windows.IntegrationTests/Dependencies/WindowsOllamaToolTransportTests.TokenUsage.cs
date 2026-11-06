using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Theory]
    [InlineData("", null, null)]
    [InlineData(",\"prompt_eval_count\":0,\"eval_count\":0", 0L, 0L)]
    [InlineData(",\"prompt_eval_count\":25", 25L, null)]
    [InlineData(",\"eval_count\":5", null, 5L)]
    [InlineData(",\"prompt_eval_count\":25,\"eval_count\":5", 25L, 5L)]
    public async Task NativeCountsRemainOptionalAndAreNotInferredFromTransportBytes(string fields, long? input, long? output)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var wire = Final[..^1] + fields + "}";
        fixture.Chat = (message, _) => Task.FromResult(Json(message, wire));
        var result = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.TokenUsage.Should().Be(input is null && output is null ? null : new ModelTokenUsage(input, output, input));
        result.Usage!.InputUtf8Bytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CompleteNativeTerminalCountsSurviveSubsequentAdmissionDenial()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message,
            Final.Replace("\"Genuine answer\"", "null", StringComparison.Ordinal)[..^1]
                + ",\"prompt_eval_count\":25,\"eval_count\":5}"));
        var result = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Message.Should().BeNull();
        result.TokenUsage.Should().Be(new ModelTokenUsage(25, 5, 25));
    }

    [Fact]
    public async Task StreamingCountsComeFromTheNativeTerminalNotTextDeltas()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var wire = AnswerChunk("answer") + "\n" + AnswerTerminal[..^1] + ",\"prompt_eval_count\":25,\"eval_count\":5}";
        var stream = new AnswerStream(Encoding.UTF8.GetBytes(wire), 1);
        fixture.Chat = (message, _) => Task.FromResult(AnswerStreaming(message, stream));
        var result = await fixture.Transport.CompleteWithAnswerAsync(fixture.Request(toolMode: ModelToolMode.None),
            (_, _) => ValueTask.CompletedTask, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.TokenUsage.Should().Be(new ModelTokenUsage(25, 5, 25));
    }

    [Fact]
    public async Task MetricsSchemaAndNativeCallUseTheHostDescriptorWithNoSessionInput()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message,
            Call.Replace(ReadOnlyCapabilityCatalog.Version, "session.get_metrics", StringComparison.Ordinal)));
        var result = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var calls = result.Message.Should().BeOfType<ModelAssistantToolCalls>().Subject;
        calls.Calls.Should().ContainSingle().Which.CapabilityId.Should().Be("session.get_metrics");
        calls.Calls[0].InputJson.Should().Be("{}");
        using var body = JsonDocument.Parse(fixture.Bodies.Single());
        var tool = body.RootElement.GetProperty("tools").EnumerateArray().Single(item =>
            item.GetProperty("function").GetProperty("name").GetString() is "session.get_metrics");
        var schema = tool.GetProperty("function").GetProperty("parameters");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.GetProperty("properties").EnumerateObject().Should().BeEmpty();
        schema.TryGetProperty("required", out _).Should().BeFalse();
    }
}
