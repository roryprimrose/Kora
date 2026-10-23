using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
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
