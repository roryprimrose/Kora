using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrameworkAgentUsesTheMaintainedNativeTransportAndOriginalCallCorrelation(bool tools)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message,
            tools && fixture.Bodies.Count == 1
                ? Call.Replace("""{"function":""", """{"id":"native_original","function":""", StringComparison.Ordinal)
                : Final));
        var envelope = new ModelContextEnvelope(fixture.RequestIdentity, "Bounded host policy", "Original task", [],
            fixture.Time.GetUtcNow(), fixture.Time.GetUtcNow().AddSeconds(15),
            tools ? ModelToolMode.ReadOnly : ModelToolMode.None,
            new(1, tools ? [ReadOnlyCapabilityCatalog.Version] : [], tools ? 1 : 0, tools ? 2 : 1, 32768, 4096, 15000));
        await using var adapter = new BoundedModelTurnAdapter(fixture.Transport, fixture.Time,
            NullLogger<BoundedModelTurnAdapter>.Instance);
        var callbacks = 0;
        var result = await adapter.RunAsync(fixture.Provenance, fixture.Profile, envelope.Serialize(), (_, _, _) =>
        {
            callbacks++;
            FunctionInvokingChatClient.CurrentContext!.CallContent.CallId.Should().Be("native_original");
            return ValueTask.FromResult(VersionReply());
        }, _ => ValueTask.FromResult(ModelTurnReason.None), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Response!.Answer.Should().Be("Genuine answer");
        callbacks.Should().Be(tools ? 1 : 0);
        fixture.Bodies.Should().HaveCount(tools ? 2 : 1);
        if (tools)
        {
            using var native = JsonDocument.Parse(fixture.Bodies[1]);
            var messages = native.RootElement.GetProperty("messages");
            messages[messages.GetArrayLength() - 1].GetProperty("tool_call_id").GetString().Should().Be("native_original");
            native.RootElement.GetProperty("tools").GetArrayLength().Should().Be(1);
        }
    }

    [Fact]
    public async Task AgentNativeModelAdvertisementContainsOnlyTheReviewedToolSubset()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var constraints = new ModelTurnConstraints(1, [ReadOnlyCapabilityCatalog.Version], 1, 2, 32768, 4096, 15000);
        var envelope = new ModelContextEnvelope(fixture.RequestIdentity, "Bounded host policy", "Original task", [],
            fixture.Time.GetUtcNow(), fixture.Time.GetUtcNow().AddSeconds(15), ModelToolMode.ReadOnly, constraints);
        var request = ModelToolProtocol.CreateRequest(fixture.Provenance, fixture.Profile,
            ModelToolProtocol.ReadContext(envelope.Serialize(), fixture.Provenance), [], 1);
        var reply = await fixture.Transport.CompleteAsync(request, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        using var native = JsonDocument.Parse(fixture.Bodies.Single());
        native.RootElement.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("function").GetProperty("name").GetString())
            .Should().Equal(ReadOnlyCapabilityCatalog.Version);
        var limitsMessage = native.RootElement.GetProperty("messages")[1];
        limitsMessage.GetProperty("role").GetString().Should().Be("system");
        using var limits = JsonDocument.Parse(limitsMessage.GetProperty("content").GetString()!);
        limits.RootElement.GetProperty("HostTurnConstraints").GetProperty("MaximumCalls").GetInt32().Should().Be(1);
        limits.RootElement.GetProperty("HostTurnConstraints").GetProperty("DeadlineMilliseconds").GetInt32().Should().Be(15000);
        limits.RootElement.GetProperty("RemainingByteBudget").GetProperty("InputUtf8Bytes").GetInt32().Should().Be(32768);
        limits.RootElement.GetProperty("RemainingByteBudget").GetProperty("OutputUtf8Bytes").GetInt32().Should().Be(4096);
        reply.Usage!.InputUtf8Bytes.Should().BeLessThanOrEqualTo(constraints.InputUtf8Bytes);
        reply.Usage.OutputUtf8Bytes.Should().BeLessThanOrEqualTo(constraints.OutputUtf8Bytes);
    }
}
