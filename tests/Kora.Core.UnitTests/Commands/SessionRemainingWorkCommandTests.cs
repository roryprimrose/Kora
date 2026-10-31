using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionRemainingWorkCommandTests
{
    [Theory]
    [InlineData("session remaining \"Build\"")]
    [InlineData("what work remains in \"Build\"")]
    [InlineData("what do you have left to do in \"Build\"")]
    [InlineData("Kora, what work remains in \"Build\"")]
    [InlineData("Kora session remaining \"Build\"")]
    public void ReadableTargetIsExplicitAndNeverASetupAlias(string text)
    {
        var command = SessionCommand.Parse(text, "Kora")!;
        command.Operation.Should().Be(SessionCommandOperation.RemainingWork);
        command.RemainingTargetName!.Value.Should().Be("Build");
        command.SessionId.Should().BeNull();
        command.IsRemainingWork.Should().BeTrue();
    }

    [Fact]
    public void ExactIdentityAndBoundedChoiceCancelPreserveBindings()
    {
        var id = Guid.NewGuid();
        SessionCommand.Parse("session remaining " + id.ToString("D"), "Kora")!.SessionId.Should().Be(id);
        SessionCommand.Parse("session remaining choose 8", "Kora")!.RemainingChoice.Should().Be(8);
        SessionCommand.Parse("session remaining cancel", "Kora")!.Operation.Should().Be(SessionCommandOperation.RemainingWorkCancel);
        SessionCommand.Parse("session remaining \"A \"\"quoted\"\" name\"", "Kora")!.RemainingTargetName!.Value.Should().Be("A \"quoted\" name");
        SessionCommand.Parse("what do you have left to do", "Kora").Should().BeNull("setup status keeps its separate existing route");
        SessionCommand.Parse("hello", "Kora").Should().BeNull();
    }

    [Theory]
    [InlineData("session remaining")]
    [InlineData("session remaining active")]
    [InlineData("session remaining current")]
    [InlineData("session remaining choose 0")]
    [InlineData("session remaining choose 9")]
    [InlineData("session remaining choose -1")]
    [InlineData("session remaining choose 1 later")]
    [InlineData("session remaining \"\"")]
    [InlineData("session remaining \"bad\"quote\"")]
    [InlineData("session remaining \"bad\nname\"")]
    [InlineData("session remaining 00000000-0000-0000-0000-000000000000")]
    [InlineData("what work remains")]
    [InlineData("what work remains for \"Build\"")]
    [InlineData("what do you have left to do in")]
    [InlineData("what do you have left to do in Build")]
    public void ReservedMalformedManagementNeverBecomesAModelRequest(string text)
    {
        var command = SessionCommand.Parse(text, "Kora")!;
        command.Operation.Should().Be(SessionCommandOperation.Invalid);
        command.Error.Should().Be(SessionCommand.RemainingWorkSyntax);
        command.IsRemainingWork.Should().BeFalse();
    }

    [Theory]
    [InlineData(121)]
    [InlineData(1100)]
    public void OversizeNameOrInputIsRejectedWithoutReinterpretation(int length) =>
        SessionCommand.Parse("session remaining \"" + new string('a', length) + "\"", "Kora")!.Operation
            .Should().Be(SessionCommandOperation.Invalid);
}