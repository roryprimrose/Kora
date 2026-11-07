using AwesomeAssertions;
using Kora.Application.Skills;

namespace Kora.Application.UnitTests.Skills;

public sealed class OwnedMcpSkillCommandTests
{
    [Theory]
    [InlineData("mcp-server")]
    [InlineData("mcp-source-revision")]
    [InlineData("mcp-tool")]
    [InlineData("mcp-schema-digest")]
    [InlineData("mcp-account")]
    [InlineData("mcp-arguments")]
    public void OriginalMcpLiteralIsNotNormalized(string field)
    {
        const string literal = "  {\"query\":\"e\u0301\"}\r\n  ";
        var command = OwnedSkillAuthoringCommand.Parse("Nova owned skill " + field + ": " + literal, "Nova");
        command.Should().Be(new OwnedSkillAuthoringCommand(field, Literal: literal));
    }

    [Theory]
    [InlineData("owned skill tool read-only-mcp", "tool")]
    [InlineData("owned skill mcp-options", "mcp-options")]
    [InlineData("owned skill tool arbitrary-mcp", "Invalid")]
    public void ExactMcpOpcodesRemainClosed(string input, string operation) =>
        OwnedSkillAuthoringCommand.Parse(input, "Nova")!.Operation.Should().Be(operation);
}
