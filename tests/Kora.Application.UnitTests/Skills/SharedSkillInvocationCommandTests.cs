using AwesomeAssertions;
using Kora.Application.Skills;

namespace Kora.Application.UnitTests.Skills;

public sealed class SharedSkillInvocationCommandTests
{
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
    public void OnlyExactHostOwnedCommandsAreSelected(string input, string? expected) =>
        SharedSkillInvocationCommand.Parse(input, "Kora")?.Action.Should().Be(expected);
}
