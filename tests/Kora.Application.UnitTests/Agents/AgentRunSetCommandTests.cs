using AwesomeAssertions;

using Kora.Application.Agents;

namespace Kora.Application.UnitTests.Agents;

public sealed class AgentRunSetCommandTests
{
    [Theory]
    [InlineData("independent agents", "Open", 0)]
    [InlineData("independent agents close", "Close", 0)]
    [InlineData("agent run 1 select", "Select", 1)]
    [InlineData("agent run 2 source selected.agent.md", "Source", 2)]
    [InlineData("agent run 1 request original task", "Request", 1)]
    [InlineData("agent run 2 target Current topic", "Target", 2)]
    [InlineData("agent run 1 targets", "Targets", 1)]
    [InlineData("agent run 2 next", "NextTargets", 2)]
    [InlineData("agent run 1 review", "Review", 1)]
    [InlineData("agent run 2 resolve", "Resolve", 2)]
    [InlineData("agent run 1 confirm", "Invoke", 1)]
    [InlineData("agent run 2 status", "Status", 2)]
    [InlineData("agent run 1 stop", "Stop", 1)]
    [InlineData("agent run 1 confirm now", "Invalid", 0)]
    [InlineData("agent run 1 source", "Invalid", 0)]
    [InlineData("agent run 2 delegate", "Invalid", 0)]
    public void RunSetFallbackKeepsExactTypedPayload(string text, string action, int number)
    {
        var command = AgentRunCommand.Parse("Kora, " + text, "Kora")!;
        command.Action.Should().Be("RunSet");
        command.RunSet!.Action.Should().Be(action);
        command.RunSet.Number.Should().Be(number);
    }

    [Theory]
    [InlineData("agent run")]
    [InlineData("agent run script")]
    [InlineData("agent run 3 confirm")]
    [InlineData("agent run 1")]
    [InlineData("independent agents now")]
    public void UnknownFamiliesDoNotReplaceExistingRoutes(string input) =>
        AgentRunCommand.Parse(input, "Kora").Should().BeNull();

    [Fact]
    public void TaskBodyRemainsExactOriginalText() =>
        AgentRunCommand.Parse("agent run 1 request   original e\u0301\r\n task ", "Kora")!.RunSet!.Argument
            .Should().Be("  original e\u0301\r\n task ");
}
