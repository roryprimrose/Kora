using AwesomeAssertions;

using Kora.Application.Agents;

namespace Kora.Application.UnitTests.Agents;

public sealed class NamedAgentCommandTests
{
    [Theory]
    [InlineData("agent invoke \"Readable role\"   original e\u0301 \r\n text  ")]
    [InlineData("Nova, agent invoke \"Readable role\"   original e\u0301 \r\n text  ")]
    public void ExactNameAndOriginalTaskAreNeverNormalized(string input)
    {
        var routed = AgentRunCommand.Parse(input, "Nova")!;
        routed.Action.Should().Be("Named");
        var command = NamedAgentCommand.Parse(routed.Argument);
        command.Name.Should().Be("Readable role");
        command.Input.Should().Be("  original e\u0301 \r\n text  ");
    }

    [Theory]
    [InlineData("agent invoke")]
    [InlineData("agent invoke ")]
    [InlineData("agent invoke not quoted")]
    [InlineData("agent invoke \"\" task")]
    [InlineData("agent invoke \"role")]
    [InlineData("agent invoke \"role\"")]
    [InlineData("agent invoke \"role\"task")]
    [InlineData("agent invoke \"role\" ")]
    [InlineData("agent invoke \"role\" \t\r\n")]
    [InlineData("agent invoke \" \" task")]
    [InlineData("agent invoke \"bad\tname\" task")]
    [InlineData("agent invoke \"role\" bad\0input")]
    public void MalformedReservedCommandsCannotFallThroughToModelOrArtifactRouting(string input)
    {
        var routed = AgentRunCommand.Parse(input, "Nova")!;
        routed.Action.Should().Be("Named");
        var parse = () => NamedAgentCommand.Parse(routed.Argument);
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void BoundsAndMalformedUnicodeRejectWithoutRepair()
    {
        var nullArgument = () => NamedAgentCommand.Parse(null!);
        nullArgument.Should().Throw<ArgumentNullException>();
        var longName = () => NamedAgentCommand.Parse($"\"{new string('n', 129)}\" task");
        longName.Should().Throw<InvalidDataException>();
        var longInput = () => NamedAgentCommand.Parse($"\"role\" {new string('x', 4097)}");
        longInput.Should().Throw<InvalidDataException>();
        var unicode = () => NamedAgentCommand.Parse("\"role\" \ud800");
        unicode.Should().Throw<InvalidDataException>();
    }
}
