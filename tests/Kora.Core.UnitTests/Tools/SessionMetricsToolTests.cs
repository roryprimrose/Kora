using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Tools;

public sealed class SessionMetricsToolTests
{
    private static SessionModelMetrics Empty => new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, 0, 0, 0, 0, []);

    [Theory]
    [InlineData("Capabilities")]
    [InlineData("Descriptor")]
    [InlineData("Version")]
    [InlineData("Readiness")]
    [InlineData("Runtimes")]
    [InlineData("Runtime")]
    [InlineData("WebPage")]
    [InlineData("Mcp")]
    [InlineData("Sessions")]
    [InlineData("Customizations")]
    [InlineData("Settings")]
    public void RejectsMixedResultShapesBeforeTheyCanBeUsed(string extra)
    {
        var node = JsonSerializer.SerializeToNode(new CapabilityReply(CapabilityOutcome.Succeeded, "observed", SessionMetrics: Empty))!;
        node[extra] = extra is "Settings" ? JsonNode.Parse("{\"Records\":[]}") : new JsonObject();
        var reply = node.Deserialize<CapabilityReply>()!;
        SessionMetricsTool.Matches(reply).Should().BeFalse();
        Action validate = () => ModelToolProtocol.ValidateObservationResult(SessionMetricsTool.Id, "{}", reply);
        validate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsMissingMetricsOversizedRowsAndCrossCapabilityPayloads()
    {
        SessionMetricsTool.Matches(new(CapabilityOutcome.Succeeded, "missing")).Should().BeFalse();
        var row = new ModelUsageSummary(ModelProviderIdentity.Ollama, "model", 1, 0, 0, 0, 0, null, null, TimeSpan.Zero);
        var excessive = Empty with { Executions = 5, Models = [row, row, row, row, row] };
        SessionMetricsTool.Matches(new(CapabilityOutcome.Succeeded, "observed", SessionMetrics: excessive)).Should().BeFalse();
        var mixed = new CapabilityReply(CapabilityOutcome.Succeeded, "observed", Version: new("1", "unknown"), SessionMetrics: Empty);
        Action validate = () => ModelToolProtocol.ValidateObservationResult(ReadOnlyCapabilityCatalog.Version, "{}", mixed);
        validate.Should().Throw<InvalidDataException>();
        var descriptor = ReadOnlyCapabilityCatalog.FindExact(ReadOnlyCapabilityCatalog.Version)!;
        CapabilityReplyWire.IsSingleDescriptor(new(CapabilityOutcome.Succeeded, "mixed", Descriptor: descriptor, SessionMetrics: Empty),
            descriptor).Should().BeFalse();
        SessionQueueReadProfilesRejectMetrics(mixed);
        Action invalidInput = () => ModelToolProtocol.CreateToolResult(new("test", SessionMetricsTool.Id, "{\"id\":\"other\"}"),
            new(CapabilityOutcome.Succeeded, "observed", SessionMetrics: Empty));
        invalidInput.Should().Throw<InvalidDataException>();
    }

    private static void SessionQueueReadProfilesRejectMetrics(CapabilityReply mixed) =>
        Kora.Core.Hosting.SessionQueueReadProfiles.Accepts(Kora.Core.Hosting.SessionQueueReadProfile.Version, mixed).Should().BeFalse();

    [Fact]
    public void TypedMetricsAreExclusiveAndRequireTheExactEmptyInputObject()
    {
        var good = new CapabilityReply(CapabilityOutcome.Succeeded, "observed", SessionMetrics: Empty);
        var call = new ModelToolCall("usage", SessionMetricsTool.Id, "{}");
        SessionMetricsTool.Matches(good).Should().BeTrue();
        ModelToolProtocol.CreateToolResult(call, good).CapabilityId.Should().Be(SessionMetricsTool.Id);
        Action missing = () => ModelToolProtocol.CreateToolResult(call, good with { SessionMetrics = null });
        missing.Should().Throw<InvalidDataException>();
        Action input = () => ModelToolProtocol.CreateToolResult(new(call.CallId, call.CapabilityId, "{\"sessionId\":\"other\"}"), good);
        input.Should().Throw<InvalidDataException>();
        foreach (var mixed in new[]
        {
            good with { Capabilities = new([], 0, null) },
            good with { Descriptor = ReadOnlyCapabilityCatalog.Descriptors[0] },
            good with { Version = new("1", "unknown") },
            good with { Readiness = new([], 0, null) },
            good with { Runtimes = new([], 0, null) },
            good with { Runtime = new("local", RuntimeLocality.Local, CapabilityAvailability.NotObserved, null, "unknown", null, false) },
            good with { WebPage = new(Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
                new("https://example.com/"), new("https://example.com/"), DateTimeOffset.UnixEpoch, "inert", false, "text/plain") },
            good with { Mcp = new("mcp.read", new string('a', 64), DateTimeOffset.UnixEpoch, "inert", null) },
            good with { Sessions = new([], 0, null) },
            good with { Customizations = new([], 0, null) },
            good with { Settings = new([], 0, null) },
        })
        { SessionMetricsTool.Matches(mixed).Should().BeFalse(); }
    }
}
