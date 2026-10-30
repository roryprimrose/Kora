using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionDispatchCommandTests
{
    private const string Id = "12345678-1234-1234-1234-123456789012";

    [Theory]
    [InlineData("session dispatch status " + Id, SessionCommandOperation.ConversationDispatchStatus)]
    [InlineData("Nova, session dispatch pause " + Id, SessionCommandOperation.ConversationDispatchPause)]
    [InlineData("Nova SESSION DISPATCH RESUME " + Id, SessionCommandOperation.ConversationDispatchResume)]
    public void ParsesExactOrdinaryConversationControl(string text, SessionCommandOperation operation)
    {
        var command = SessionCommand.Parse(text, "Nova")!;
        command.Operation.Should().Be(operation);
        command.IsConversationDispatch.Should().BeTrue();
        command.SessionId.Should().Be(Guid.Parse(Id));
    }

    [Theory]
    [InlineData("session dispatch")]
    [InlineData("session dispatch pause selected")]
    [InlineData("session dispatch pause 00000000-0000-0000-0000-000000000000")]
    [InlineData("session dispatch pause " + Id + " extra")]
    [InlineData("session dispatch reorder " + Id)]
    [InlineData("session dispatch\tpause " + Id)]
    public void RejectsMalformedControlWithoutModelFallback(string text)
    {
        var command = SessionCommand.Parse(text, "Nova")!;
        command.Operation.Should().Be(SessionCommandOperation.Invalid);
        command.IsConversationDispatch.Should().BeFalse();
        command.Error.Should().Contain("never the diagnostic queue");
    }

    [Fact]
    public void OversizedDispatchControlIsRejected() =>
        SessionCommand.Parse("session dispatch pause " + new string('x', 1024), "Nova")!.Operation
            .Should().Be(SessionCommandOperation.Invalid);
}
