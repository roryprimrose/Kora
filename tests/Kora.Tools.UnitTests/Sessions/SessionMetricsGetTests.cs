using System.Collections.Immutable;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Tools;
using Kora.Tools.Sessions;

namespace Kora.Tools.UnitTests.Sessions;

public sealed class SessionMetricsGetTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(6)]
    public void ReturnsBoundedModelRowsWithSessionWideTotalsAndNoTargetInput(int models)
    {
        var rows = Enumerable.Range(0, models).Select(index => new ModelUsageSummary(ModelProviderIdentity.Ollama,
            "model-" + index, 1, 1, 1, 100, 25, 100, 1000, TimeSpan.Zero)).ToImmutableArray();
        var snapshot = new SessionModelMetrics(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, models, models, models,
            models * 100, models * 25, rows);
        var result = SessionMetricsGet.Execute(snapshot, "{}");
        result.Outcome.Should().Be(CapabilityOutcome.Succeeded);
        result.SessionMetrics!.InputTokens.Should().Be(models * 100);
        result.SessionMetrics.Models.Should().HaveCount(Math.Min(models, SessionMetricsTool.MaximumModelRows));
        result.SessionMetrics.OmittedModels.Should().Be(Math.Max(0, models - SessionMetricsTool.MaximumModelRows));
        SessionMetricsTool.Matches(result).Should().BeTrue();
        ModelToolProtocol.ValidateRetainedReadOnlyResult(SessionMetricsTool.Id, "{}",
            System.Text.Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(result)));
    }

    [Theory]
    [InlineData("{\"session\":\"other\"}")]
    [InlineData("[]")]
    [InlineData("invalid")]
    public void DeniesModelSelectedTargets(string input)
    {
        SessionMetricsGet.Execute(new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, 0, 0, 0, 0, []), input)
            .Outcome.Should().Be(CapabilityOutcome.Denied);
    }

    [Fact]
    public void OversizedCompleteMetricsWireIsExplicitFailureNotTruncatedSuccess()
    {
        var row = new ModelUsageSummary(ModelProviderIdentity.Ollama, new string('<', 256),
            1, 1, 1, 100, 25, 100, 1000, TimeSpan.Zero);
        var snapshot = new SessionModelMetrics(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 4, 4, 4, 400, 100,
            [row, row, row, row]);
        var reply = SessionMetricsGet.Execute(snapshot, "{}");
        reply.Outcome.Should().Be(CapabilityOutcome.Failed);
        reply.Reason.Should().Be("output-limit-exceeded");
        reply.SessionMetrics.Should().BeNull();
    }
}
