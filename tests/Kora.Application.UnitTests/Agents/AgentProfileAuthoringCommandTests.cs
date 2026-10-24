using AwesomeAssertions;

using Kora.Application.Agents;

namespace Kora.Application.UnitTests.Agents;

public sealed class AgentProfileAuthoringCommandTests
{
    [Theory]
    [InlineData("agent author", AgentProfileAuthoringAction.Open, "")]
    [InlineData("agent author create example", AgentProfileAuthoringAction.Create, "example")]
    [InlineData("agent author source ---\nExact complete source\n", AgentProfileAuthoringAction.Stage, "---\nExact complete source\n")]
    [InlineData("agent author validate", AgentProfileAuthoringAction.Validate, "")]
    [InlineData("agent author save as new", AgentProfileAuthoringAction.ReviewSave, "")]
    [InlineData("agent author list", AgentProfileAuthoringAction.List, "")]
    [InlineData("agent author inspect example", AgentProfileAuthoringAction.Inspect, "example")]
    [InlineData("agent author cancel review", AgentProfileAuthoringAction.CancelReview, "")]
    public void TypedAndCurrentNameActivationShareTypedAuthoringPayload(string text, AgentProfileAuthoringAction action, string argument)
    {
        foreach (var prefix in new[] { "", "Kora, " })
        {
            var command = AgentRunCommand.Parse(prefix + text, "Kora");
            command!.Authoring.Should().Be(new AgentProfileAuthoringCommand(action, argument));
            command.Action.Should().Be("Authoring");
        }
    }

    [Theory]
    [InlineData("agent author confirm save as new")]
    [InlineData("agent author delete example")]
    [InlineData("agent author rename example other")]
    [InlineData("agent author save as new extra")]
    [InlineData("agent author create")]
    [InlineData("other")]
    [InlineData("")]
    public void NoTypedOrVoiceMutationConfirmationOrArbitraryOperation(string input) =>
        AgentRunCommand.Parse(input, "Kora").Should().BeNull();

    [Fact]
    public void InvalidDestinationCannotBecomePathAuthority()
    {
        var parse = () => AgentRunCommand.Parse("agent author create ..\\elsewhere", "Kora");
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("agent source c:\\existing.agent.md", "Source", "c:\\existing.agent.md")]
    [InlineData("agent request original request", "Request", "original request")]
    [InlineData("agent confirm invocation", "Invoke", "")]
    public void ExistingRunRoutesAreUnchanged(string text, string action, string argument)
    {
        var parsed = AgentRunCommand.Parse(text, "Kora");
        parsed.Should().Be(new AgentRunCommand(action, argument));
        parsed!.Authoring.Should().BeNull();
    }
}
