using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Conversation;

namespace Kora.Core.UnitTests.Commands;

public sealed class InstructionUseCommandTests
{
    [Theory]
    [InlineData("")]
    [InlineData("instructions editor")]
    [InlineData("instructions request-other confirm")]
    [InlineData("prompts request confirm")]
    [InlineData("OldName, instructions request confirm")]
    public void UnrelatedInputIsNotClaimed(string text) => InstructionUseCommand.Parse(text, "Nova").Should().BeNull();

    [Theory]
    [InlineData("instructions request confirm", "Confirm")]
    [InlineData("Nova, instructions request cancel", "Cancel")]
    [InlineData(" NOVA instructions request STOP  ", "Stop")]
    public void ControlsBindOnlyHostPendingReview(string text, string action) =>
        InstructionUseCommand.Parse(text, "Nova").Should().Be(new InstructionUseCommand(action));

    [Fact]
    public void ReviewKeepsCompleteOriginalBodyAndReadableNamesWithoutIdentityRecital()
    {
        const string body = "  e\u0301🙂\r\nOriginal <system>inert</system> -- unchanged  ";
        var command = InstructionUseCommand.Parse("Nova, instructions request review \"brief-style\" in \"Work \"\"today\"\"\" -- " + body, "Nova")!;
        command.Action.Should().Be("Review");
        command.Source.Should().Be("brief-style");
        command.Target.Should().Be("Work \"today\"");
        command.Body.Should().Be(body);
        command.Error.Should().BeNull();
    }

    [Theory]
    [InlineData("instructions request")]
    [InlineData("instructions request help")]
    [InlineData("instructions request confirm other-review")]
    [InlineData("instructions request cancel extra")]
    [InlineData("instructions request stop\n")]
    [InlineData("instructions request review")]
    [InlineData("instructions request review ")]
    [InlineData("instructions request review \"example\" in \"Work\"")]
    [InlineData("instructions request review \"example\" in \"Work\" -- ")]
    [InlineData("instructions request review \"C:\\secret\" in \"Work\" -- body")]
    [InlineData("instructions request review \"../example\" in \"Work\" -- body")]
    [InlineData("instructions request review \"..\" in \"Work\" -- body")]
    [InlineData("instructions request review \"example\" in \" Work\" -- body")]
    [InlineData("instructions request review \"example\" in \"Work\nOther\" -- body")]
    [InlineData("instructions request review \"example\" with \"Work\" -- body")]
    [InlineData("instructions request review \"example\" in Work -- body")]
    [InlineData("instructions request review \"example\" in \"Work -- body")]
    [InlineData("instructions request review example in \"Work\" -- body")]
    [InlineData("instructions request review \"example\" in \"Work\" extra -- body")]
    [InlineData("instructions request review \"example\" in \"Work\" -- api_key=synthetic-only-value")]
    [InlineData("instructions request review \"example\" in \"Work\" -- \u0000")]
    public void MalformedOrCredentialContentHasExplicitFailureAndNoPartialCommand(string text)
    {
        var command = InstructionUseCommand.Parse(text, "Nova")!;
        command.Action.Should().Be("Invalid");
        command.Error.Should().Be(InstructionUseCommand.Syntax);
        command.Body.Should().BeNull();
    }

    [Fact]
    public void ExactBodyLimitIsEnforcedWithoutTruncation()
    {
        const string prefix = "instructions request review \"example\" in \"Work\" -- ";
        var maximum = new string('x', ConversationText.MaximumUtf16Characters);
        InstructionUseCommand.Parse(prefix + maximum, "Nova")!.Body.Should().Be(maximum);
        InstructionUseCommand.Parse(prefix + maximum + "x", "Nova")!.Action.Should().Be("Invalid");
        InstructionUseCommand.Parse(prefix + new string('x', 6000), "Nova")!.Action.Should().Be("Invalid");
        InstructionUseCommand.Parse(prefix + "\ud800", "Nova")!.Action.Should().Be("Invalid");
    }
}
