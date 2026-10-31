using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionRequestTaskOutputTests
{
    private const string Target = "12345678-1234-1234-1234-123456789012";

    [Theory]
    [InlineData("Inherit", TaskResponseMode.Inherit)]
    [InlineData("visualonly", TaskResponseMode.VisualOnly)]
    [InlineData("Hybrid", TaskResponseMode.Hybrid)]
    [InlineData("VoiceOnly", TaskResponseMode.VoiceOnly)]
    public void OutputReviewPreservesCompleteOriginalInputAndExactTarget(string declared, TaskResponseMode mode)
    {
        const string body = "  Original e\u0301🙂\r\nend -- data  ";
        var command = SessionCommand.Parse("Nova, session request output " + Target + " 2 0 " + declared + " -- " + body, "Nova")!;
        command.Operation.Should().Be(SessionCommandOperation.RequestReview);
        command.RequestTaskResponseMode.Should().Be(mode);
        command.RequestBody.Should().Be(body);
        command.SessionId.Should().Be(Guid.Parse(Target));
        command.Generation.Should().Be(2);
        command.MetadataRevision.Should().Be(0);
        command.RequestLocalProfile.Should().BeNull();
    }

    [Theory]
    [InlineData("output")]
    [InlineData("output " + Target + " 1 0 VoiceOnly")]
    [InlineData("output " + Target + " 1 0 3 -- body")]
    [InlineData("output " + Target + " 1 0 99 -- body")]
    [InlineData("output " + Target + " 1 0 unknown -- body")]
    [InlineData("output " + Target + " 0 0 VoiceOnly -- body")]
    [InlineData("output active 1 0 VoiceOnly -- body")]
    [InlineData("output \"named\" 1 0 VoiceOnly -- body")]
    [InlineData("output " + Target + " 1 0 VoiceOnly extra -- body")]
    [InlineData("output " + Target + " 1 0 VoiceOnly -- ")]
    public void InvalidOutputReviewNeverFallsBackOrChangesScope(string tail) =>
        SessionCommand.Parse("session request " + tail, "Nova")!.Operation.Should().Be(SessionCommandOperation.Invalid);
}
