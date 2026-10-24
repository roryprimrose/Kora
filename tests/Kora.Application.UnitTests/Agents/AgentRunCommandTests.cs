using AwesomeAssertions;

using Kora.Application.Agents;

namespace Kora.Application.UnitTests.Agents;

public sealed class AgentRunCommandTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData("ordinary input", null)]
    [InlineData("review agent", "Open")]
    [InlineData("agent targets", "Targets")]
    [InlineData("agent next targets", "NextTargets")]
    [InlineData("agent target exact-id", "Target")]
    [InlineData("agent source C:\\role.agent.md", "Source")]
    [InlineData("agent request work", "Request")]
    [InlineData("agent review invocation", "Review")]
    [InlineData("agent review references", "Review")]
    [InlineData("agent consent resolution", "Resolve")]
    [InlineData("agent consent resolution now", null)]
    [InlineData("agent confirm invocation", "Invoke")]
    [InlineData("agent stop", "Stop")]
    [InlineData("agent run script", null)]
    [InlineData("agent confirm invocation now", null)]
    public void CommandsSelectOnlyTheExactReviewedRoute(string input, string? action) =>
        AgentRunCommand.Parse(input, "Kora")?.Action.Should().Be(action);

    [Fact]
    public void OriginalRequestProseAndUnicodeAreNotNormalizedOrReplacedByRouting() =>
        AgentRunCommand.Parse("Kora, agent request   untouched e\u0301 \r\n data ", "Kora")!
            .Argument.Should().Be("  untouched e\u0301 \r\n data ");
}
