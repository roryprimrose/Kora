using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class ClaudeMessagesRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedAnswersAndPostResponseCancellationKeepActualUsageWithoutUsingThePreflightCount(bool cancel)
    {
        var fixture = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = fixture.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = fixture.Profile(catalogue);
        var context = new ModelContextEnvelope(root.Request, "policy", "request", [], fixture.Time.GetUtcNow(),
            fixture.Time.GetUtcNow().AddMinutes(1), ModelToolMode.None);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection);
        var received = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        fixture.Respond = message =>
        {
            if (message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal))
            { return ClaudeAccountBrokerTests.Fixture.Json("""{"input_tokens":100}"""); }
            received = true;
            return ClaudeAccountBrokerTests.Fixture.Json(cancel ? Answer
                : Answer.Replace("\"type\":\"text\"", "\"type\":\"thinking\"", StringComparison.Ordinal));
        };
        await using var runtime = new ClaudeMessagesRuntime(broker, fixture.Time,
            NullLogger<ClaudeMessagesRuntime>.Instance, fixture.CreateHttp);
        var result = await runtime.RunAsync(provenance, profile, context, (_, _) => ValueTask.FromResult(true), _ =>
        {
            if (received && cancel) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            return ValueTask.FromResult(true);
        }, cancellation.Token);
        result.Outcome.Should().Be(cancel ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Unknown);
        result.Answer.Should().BeNull();
        result.TokenUsage.Should().Be(new ModelTokenUsage(25, 5, 25));
        runtime.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public void MetricsDescriptorTranslatesToAnEmptyNativeSchemaAndBackToTheExactHostTool()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var profile = new ProviderProfileTuple(ProviderService.Claude, new("account"), new(1),
            new("exact-model"), new("disabled"), new("8192"));
        var context = new ModelContextEnvelope(request, "policy", "request", [], DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMinutes(1), ModelToolMode.ReadOnly,
            ClaudeObservationTools.Constrain(["session.get_metrics"]));
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request,
            new(ModelProviderIdentity.Claude, new(Guid.NewGuid()), new(1)));
        var turn = ModelToolProtocol.CreateRequest(provenance, profile,
            ModelToolProtocol.ReadContext(context.Serialize(), provenance), [], 1);
        using var body = JsonDocument.Parse(ClaudeObservationWire.Serialize(turn, 4096));
        var tool = body.RootElement.GetProperty("tools").EnumerateArray().Single();
        tool.GetProperty("name").GetString().Should().Be("kora_session_get_metrics");
        var schema = tool.GetProperty("input_schema");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.GetProperty("properties").EnumerateObject().Should().BeEmpty();
        schema.GetProperty("required").GetArrayLength().Should().Be(0);
        var native = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = "message", type = "message", role = "assistant", model = "exact-model",
            content = new[] { new { type = "tool_use", id = "metrics", name = "kora_session_get_metrics", input = new { } } },
            stop_reason = "tool_use", stop_sequence = (string?)null, usage = new { input_tokens = 25, output_tokens = 5 },
        });
        var calls = ClaudeObservationWire.Parse(native, profile, 4096, 8192, turn.Context.Tools)
            .Should().BeOfType<ModelAssistantToolCalls>().Subject;
        calls.Calls.Should().ContainSingle().Which.CapabilityId.Should().Be("session.get_metrics");
        calls.Calls[0].InputJson.Should().Be("{}");
    }
}
