using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionInstructionScopeCommandTests
{
    [Theory]
    [InlineData("session instruction apply \"example\"", SessionCommandOperation.InstructionScopeReview)]
    [InlineData("Kora, session instruction confirm", SessionCommandOperation.InstructionScopeConfirm)]
    [InlineData("session instruction stop", SessionCommandOperation.InstructionScopeStop)]
    [InlineData("session instruction status", SessionCommandOperation.InstructionScopeStatus)]
    public void ParsesBoundedScopeWithoutExposingInternalIdentities(string text, SessionCommandOperation operation)
    {
        var result = SessionCommand.Parse(text, "Kora")!;
        result.Operation.Should().Be(operation);
        result.IsInstructionScope.Should().BeTrue();
        result.SessionId.Should().BeNull();
        if (operation == SessionCommandOperation.InstructionScopeReview) { result.ScopeInstructionName.Should().Be("example"); }
    }

    [Theory]
    [InlineData("session instruction")]
    [InlineData("session instruction apply example")]
    [InlineData("session instruction apply \" example\"")]
    [InlineData("session instruction apply \"\"")]
    [InlineData("session instruction apply \"exa\"mple\"")]
    [InlineData("session instruction apply \"example\" extra")]
    [InlineData("session instruction confirm extra")]
    [InlineData("session instruction stop\n")]
    public void RejectsAmbiguousOrUnboundedInput(string text) =>
        SessionCommand.Parse(text, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);

    [Fact]
    public void RejectsOversizedInputAndDoesNotRelabelUnrelatedSessionOperations()
    {
        var oversized = "session instruction apply \"" + new string('x', SessionCommand.MaximumInputBytes) + "\"";
        SessionCommand.Parse(oversized, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("session list", "Kora")!.IsInstructionScope.Should().BeFalse();
    }
}
