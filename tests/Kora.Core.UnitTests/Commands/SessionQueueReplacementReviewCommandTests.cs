using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionQueueReplacementReviewCommandTests
{
    private const string Session = "12345678-1234-1234-abcd-123456789012";
    private const string TaskId = "abcdef12-1234-1234-abcd-123456789012";
    private const string Input = "session queue replacement review " + Session + " " + TaskId;

    [Theory]
    [InlineData("")]
    [InlineData("Kora, ")]
    [InlineData("kora ")]
    public void ExactGrammarAdmitsOnlyCanonicalNonemptyIds(string prefix)
    {
        var command = SessionQueueReplacementReviewCommand.Parse(prefix + Input, "Kora");
        command.Should().Be(new SessionQueueReplacementReviewCommand(Guid.Parse(Session), Guid.Parse(TaskId)));
        SessionCommand.Syntax.Should().Contain(SessionQueueReplacementReviewCommand.Syntax);
    }

    [Theory]
    [InlineData("session queue replacement")]
    [InlineData("session queue replacement review")]
    [InlineData("session queue replacement confirm")]
    [InlineData("session queue replacement confirm 12345678-1234-1234-abcd-123456789012 abcdef12-1234-1234-abcd-123456789012")]
    [InlineData("session queue replacement status")]
    [InlineData("session queue replacement review selected latest")]
    [InlineData("session queue replacement review 12345678-1234-1234-abcd-123456789012 ABCDEF12-1234-1234-abcd-123456789012")]
    [InlineData("session queue replacement review 12345678-1234-1234-ABCD-123456789012 abcdef12-1234-1234-abcd-123456789012")]
    [InlineData("session queue replacement review 00000000-0000-0000-0000-000000000000 abcdef12-1234-1234-abcd-123456789012")]
    [InlineData("session queue replacement review 12345678-1234-1234-abcd-123456789012 00000000-0000-0000-0000-000000000000")]
    [InlineData("session queue replacement help extra")]
    [InlineData("session\tqueue replacement help")]
    public void ReservedMalformedGrammarNeverFallsThrough(string input) =>
        SessionQueueReplacementReviewCommand.Parse(input, "Kora")!.Error.Should().Contain("Separate native confirmation");

    [Theory]
    [InlineData("session")]
    [InlineData("session queue")]
    [InlineData("session queue cancellation review")]
    [InlineData("queue requeue-expired-review")]
    [InlineData("session requests replacement")]
    [InlineData("help queue replacement")]
    [InlineData("Nova, session queue replacement help")]
    [InlineData("Korax session queue replacement help")]
    public void UnrelatedAndLegacyRoutesRemainSeparate(string input) =>
        SessionQueueReplacementReviewCommand.Parse(input, "Kora").Should().BeNull();

    [Fact]
    public void HelpIsPassiveAndExactByteBoundAndControlsAreEnforced()
    {
        SessionQueueReplacementReviewCommand.Parse("SESSION QUEUE REPLACEMENT HELP", "Kora")!
            .Should().Be(new SessionQueueReplacementReviewCommand(Guid.Empty, Guid.Empty, IsHelp: true));
        SessionQueueReplacementReviewCommand.Parse(Input + new string(' ', 1024 - Input.Length), "Kora")!.Error.Should().BeNull();
        SessionQueueReplacementReviewCommand.Parse(Input + new string(' ', 1025 - Input.Length), "Kora")!.Error.Should().NotBeNull();
        SessionQueueReplacementReviewCommand.Parse(Input + " extra", "Kora")!.Error.Should().NotBeNull();
        SessionQueueReplacementReviewCommand.Parse(Input + "\n", "Kora")!.Error.Should().NotBeNull();
        SessionQueueReplacementReviewCommand.Parse(Input + new string('é', 512), "Kora")!.Error.Should().NotBeNull();
    }
}
