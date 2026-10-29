using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionRequestLocalProfileTests
{
    private const string Target = "12345678-1234-1234-1234-123456789012";

    [Theory]
    [InlineData("")]
    [InlineData("Nova, ")]
    public void OnceRequiresCompleteExactLocalTupleAndPreservesOriginalInput(string prefix)
    {
        const string body = "  Original e\u0301🙂\r\nend -- data  ";
        var command = SessionCommand.Parse(prefix + "session request once " + Target
            + " 2 0 account 3 model reasoning context -- " + body, "Nova")!;
        command.Operation.Should().Be(SessionCommandOperation.RequestReview);
        command.RequestLocalProfile.Should().Be(new ProviderProfileTuple(ProviderService.Local, new("account"),
            new(3), new("model"), new("reasoning"), new("context")));
        command.RequestBody.Should().Be(body);
        command.SessionId.Should().Be(Guid.Parse(Target));
        command.Generation.Should().Be(2);
        command.MetadataRevision.Should().Be(0);
    }

    [Theory]
    [InlineData("once")]
    [InlineData("once " + Target + " 1 0 account 1 model reasoning context")]
    [InlineData("once " + Target + " 1 0 account 1 model reasoning -- body")]
    [InlineData("once " + Target + " 1 0 account 0 model reasoning context -- body")]
    [InlineData("once " + Target + " 1 0 account -1 model reasoning context -- body")]
    [InlineData("once " + Target + " 1 0 account 1 model reasoning context extra -- body")]
    [InlineData("once " + Target + " 0 0 account 1 model reasoning context -- body")]
    [InlineData("once " + Target + " 1 0 accøunt 1 model reasoning context -- body")]
    [InlineData("once active 1 0 account 1 model reasoning context -- body")]
    [InlineData("once " + Target + " 1 0 account 1 model reasoning context -- ")]
    [InlineData("once " + Target + " 1 0 account 1 model reasoning context -- api_key=synthetic-only-value")]
    public void MalformedOnceNeverFallsBackToOrdinaryOrHostedRequest(string tail) =>
        SessionCommand.Parse("session request " + tail, "Nova")!.Operation.Should().Be(SessionCommandOperation.Invalid);
}
