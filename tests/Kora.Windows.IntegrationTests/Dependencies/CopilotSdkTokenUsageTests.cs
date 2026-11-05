using AwesomeAssertions;
using GitHub.Copilot;
using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class CopilotSdkTokenUsageTests
{
    [Fact]
    public void AuthoritativePerCallEventsProduceTotalsAndLatestRequestInputNotWindowOccupancy()
    {
        var usage = new CopilotSdkClient.Usage("selected");
        usage.Observe(Report(10, 2, 100));
        usage.Observe(Report(30, 4, 200));
        usage.Snapshot(2).Should().Be(new ModelTokenUsage(40, 6, 30));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(100L)]
    public void PromptAdmissionLimitDoesNotSupplyModelContextWindowCapacity(long? promptLimit)
    {
        var usage = new CopilotSdkClient.Usage("selected");
        usage.Observe(Report(10, 2, promptLimit));
        usage.Snapshot(1).Should().Be(new ModelTokenUsage(10, 2, 10));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingFieldsCannotBecomePartialTotals(bool missingInput)
    {
        var usage = new CopilotSdkClient.Usage("selected");
        usage.Observe(Report(10, 2));
        usage.Observe(Report(missingInput ? null : 30, missingInput ? 4 : null));
        usage.Snapshot(2).Should().Be(new ModelTokenUsage(missingInput ? null : 40,
            missingInput ? 6 : null, missingInput ? null : 30));
    }

    [Fact]
    public void MissingEventsAndUnexpectedExtraEventsNeverClaimCompleteTotals()
    {
        var usage = new CopilotSdkClient.Usage("selected");
        usage.Snapshot(0).Should().BeNull();
        usage.Snapshot(1).Should().BeNull();
        usage.Observe(Report(10, 2));
        usage.Snapshot(0).Should().BeNull();
        usage.Snapshot(2).Should().BeNull();
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("duplicate")]
    public void SubagentAndRepeatedEventsCannotInflateAdmittedTurnTotals(string kind)
    {
        var usage = new CopilotSdkClient.Usage("selected");
        var report = Report(10, 2);
        usage.Observe(report);
        var extra = kind is "duplicate" ? report : Report(30, 4);
        if (kind is "agent") { extra.AgentId = "foreign"; }
        usage.Observe(extra);
        usage.Snapshot(1).Should().Be(new ModelTokenUsage(10, 2, 10));
    }

    [Theory]
    [InlineData("model")]
    [InlineData("negative-input")]
    [InlineData("negative-output")]
    [InlineData("input-overflow")]
    [InlineData("output-overflow")]
    public void InvalidReportsAreSurfacedOutsideTheSdkCallback(string fault)
    {
        var usage = new CopilotSdkClient.Usage("selected");
        var report = Report(fault is "input-overflow" ? long.MaxValue : 10,
            fault is "output-overflow" ? long.MaxValue : 2);
        usage.Observe(report);
        var invalid = Report(fault is "negative-input" ? -1 : 1, fault is "negative-output" ? -1 : 1);
        if (fault is "model") { invalid.Data.Model = "foreign"; }
        usage.Observe(invalid);
        Assert.Throws<InvalidDataException>(() => usage.Snapshot(2));
    }

    internal static AssistantUsageEvent Report(long? input, long? output, long? promptLimit = null) => new()
    {
        Id = Guid.NewGuid(),
        Data = new() { Model = "selected", InputTokens = input, OutputTokens = output, MaxPromptTokens = promptLimit },
    };
}
