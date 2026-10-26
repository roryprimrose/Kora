using AwesomeAssertions;
using Kora.Application.Skills;

namespace Kora.Application.UnitTests.Skills;

public sealed class SharedSkillInvocationCommandTests
{
    [Theory]
    [InlineData("review tool skill page-guide", "Review")]
    [InlineData("tool skill review tool revision", "ReviewToolRevision")]
    [InlineData("tool skill enable", "Enable")]
    [InlineData("tool skill confirm invocation", "Invoke")]
    [InlineData("tool skill address https://example.com/", "Address")]
    [InlineData("tool skill second address https://second.example/", "SecondAddress")]
    [InlineData("Kora, tool skill second address https://second.example/", "SecondAddress")]
    [InlineData("tool skill clear second address", "SecondAddress")]
    public void ToolCapableCommandsAreExplicitlyDistinct(string input, string action)
    {
        var command = SharedSkillInvocationCommand.Parse(input, "Kora")!;
        command.Action.Should().Be(action);
        command.ToolCapable.Should().BeTrue();
    }

    [Fact]
    public void OversizedCommandNeverAdmitsTruncatedRequest() =>
        SharedSkillInvocationCommand.Parse("shared skill request " + new string('x', Kora.Core.Commands.SessionCommand.MaximumInputBytes), "Kora")!
            .Action.Should().Be("Invalid");

    [Theory]
    [InlineData("shared skill request   e\u0301 \r\n untouched  ", "  e\u0301 \r\n untouched  ")]
    [InlineData("Kora, shared skill request   original  ", "  original  ")]
    public void RequestProseRemainsUnchangedAndDistinctFromRoutingPrefix(string input, string expected)
    {
        var command = SharedSkillInvocationCommand.Parse(input, "Kora")!;
        command.Action.Should().Be("Request");
        command.Argument.Should().Be(expected);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("ordinary request", null)]
    [InlineData("review shared skill concise-review", "Review")]
    [InlineData("shared skill enable", "Enable")]
    [InlineData("shared skill targets", "Targets")]
    [InlineData("shared skill next targets", "NextTargets")]
    [InlineData("shared skill target exact-id", "Target")]
    [InlineData("shared skill review invocation", "Prepare")]
    [InlineData("shared skill confirm invocation", "Invoke")]
    [InlineData("shared skill close", "Close")]
    [InlineData("shared skill run script", "Invalid")]
    [InlineData("shared skill review tool revision", "Invalid")]
    public void OnlyExactHostOwnedCommandsAreSelected(string input, string? expected) =>
        SharedSkillInvocationCommand.Parse(input, "Kora")?.Action.Should().Be(expected);
}
