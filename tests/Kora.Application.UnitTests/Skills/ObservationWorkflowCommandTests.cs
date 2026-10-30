using AwesomeAssertions;
using Kora.Application.Skills;

namespace Kora.Application.UnitTests.Skills;

public sealed class ObservationWorkflowCommandTests
{
    [Fact]
    public void GenerationAndObservationFixedPhrasesRemainSeparateAdmittedRoutes()
    {
        OwnedSkillAuthoringCommand.FixedPhrases.Should().Contain("owned skill generate")
            .And.Contain("owned skill workflow invoke");
        OwnedSkillAuthoringCommand.Parse("owned skill generate", "Nova")!.Operation.Should().Be("generate");
        OwnedSkillAuthoringCommand.Parse("owned skill workflow invoke", "Nova")!.Operation.Should().Be("workflow");
        var literal = OwnedSkillAuthoringCommand.Parse("create a skill that owned skill workflow invoke", "Nova")!;
        literal.Operation.Should().Be("generate-request");
        literal.Literal.Should().Be("owned skill workflow invoke");
    }

    [Theory]
    [InlineData("editor", "editor")]
    [InlineData("CREATE example 1.0.0", "create")]
    [InlineData("reopen example 1.0.0", "reopen")]
    [InlineData("review example 1.0.0", "review")]
    [InlineData("name: Literal", "name")]
    [InlineData("description: Literal", "description")]
    [InlineData("definition: {\"steps\":[]}", "definition")]
    [InlineData("request:  unchanged\r\n ", "request")]
    [InlineData("validate", "validate")]
    [InlineData("save", "save")]
    [InlineData("list", "list")]
    [InlineData("targets", "targets")]
    [InlineData("next-targets", "next-targets")]
    [InlineData("target 12345678-1234-1234-1234-123456789abc", "target")]
    [InlineData("enable", "enable")]
    [InlineData("invoke", "invoke")]
    [InlineData("approve-step", "approve-step")]
    [InlineData("deny-step", "deny-step")]
    [InlineData("status", "status")]
    [InlineData("cancel", "cancel")]
    [InlineData("close", "close")]
    [InlineData("", "Invalid")]
    [InlineData("other", "Invalid")]
    [InlineData("target invalid", "Invalid")]
    [InlineData("target 12345678-1234-1234-1234-123456789ABC", "Invalid")]
    [InlineData("create CON 1.0.0", "Invalid")]
    [InlineData("create example", "Invalid")]
    [InlineData("create example 1.0.0 extra", "Invalid")]
    [InlineData("other example 1.0.0", "Invalid")]
    [InlineData("cancel\n", "Invalid")]
    public void DedicatedGrammarDoesNotInterpretPayloadOrInventOperations(string text, string expected) =>
        ObservationWorkflowCommand.Parse(text).Operation.Should().Be(expected);

    [Fact]
    public void LiteralAndActivationRoutesKeepTheCompleteUnchangedPayload()
    {
        const string literal = "  {\"steps\":[]}\r\nowned skill workflow invoke  ";
        var command = OwnedSkillAuthoringCommand.Parse("Nova, owned skill workflow definition: " + literal, "Nova")!;
        command.Operation.Should().Be("workflow");
        ObservationWorkflowCommand.Parse(command.Literal!).Literal.Should().Be(literal);
        ObservationWorkflowCommand.Syntax.Should().Contain("approve-step").And.Contain("cancel");
    }
}
