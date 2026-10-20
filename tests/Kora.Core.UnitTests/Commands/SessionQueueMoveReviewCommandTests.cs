using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionQueueMoveReviewCommandTests
{
    private const string Session = "12345678-1234-1234-abcd-123456789012";
    private const string Task = "12345678-1234-1234-abcd-123456789013";
    private const string Neighbor = "12345678-1234-1234-abcd-123456789014";
    private const string Pair = Session + " " + Task + " " + Neighbor;
    private const string Valid = "session queue move review " + Pair + " up";

    [Theory]
    [InlineData(Valid, "Kora", true)]
    [InlineData("Nova, " + Valid, "Nova", true)]
    [InlineData("nova " + Valid, "Nova", true)]
    [InlineData("  SESSION  QUEUE MOVE REVIEW " + Pair + " down ", "Kora", false)]
    public void OnlyExactPairAndDirectionSelectTheReview(string input, string name, bool up) =>
        SessionQueueMoveReviewCommand.Parse(input, name).Should()
            .Be(new SessionQueueMoveReviewCommand(Guid.Parse(Session), Guid.Parse(Task), Guid.Parse(Neighbor), up));

    [Theory]
    [InlineData("")]
    [InlineData("Nova")]
    [InlineData("NovaX, " + Valid)]
    [InlineData("Nova:" + Valid)]
    [InlineData("Kora, " + Valid)]
    [InlineData("session")]
    [InlineData("session queue")]
    [InlineData("session queue cancellation review")]
    [InlineData("session queue replacement review")]
    [InlineData("session request help")]
    [InlineData("queue move up")]
    [InlineData("task queue move")]
    [InlineData("session other move")]
    public void OtherGrammarFamiliesAndOldNamesAreNotClaimed(string input) =>
        SessionQueueMoveReviewCommand.Parse(input, "Nova").Should().BeNull();

    [Theory]
    [InlineData("session queue move")]
    [InlineData("session queue move help")]
    [InlineData("session queue move confirm " + Pair + " up")]
    [InlineData("session queue move review " + Pair)]
    [InlineData("session queue move review " + Pair + " up confirm")]
    [InlineData("session queue move review " + Pair + " UP")]
    [InlineData("session queue move review " + Pair + " sideways")]
    [InlineData("session queue move review " + Session + " " + Task + " " + Task + " up")]
    [InlineData("session queue move review selected " + Task + " " + Neighbor + " up")]
    [InlineData("session queue move review " + Session + " current " + Neighbor + " up")]
    [InlineData("session queue move review " + Session + " " + Task + " latest up")]
    [InlineData("session queue move review 00000000-0000-0000-0000-000000000000 " + Task + " " + Neighbor + " up")]
    [InlineData("session queue move review " + Session + " 00000000-0000-0000-0000-000000000000 " + Neighbor + " up")]
    [InlineData("session queue move review " + Session + " " + Task + " 00000000-0000-0000-0000-000000000000 up")]
    [InlineData("session queue move review 12345678-1234-1234-ABCD-123456789012 " + Task + " " + Neighbor + " up")]
    [InlineData("session queue move review " + Session + " 12345678-1234-1234-ABCD-123456789013 " + Neighbor + " up")]
    [InlineData("session queue move review " + Session + " " + Task + " 12345678-1234-1234-ABCD-123456789014 up")]
    [InlineData("session queue move review {12345678-1234-1234-abcd-123456789012} " + Task + " " + Neighbor + " up")]
    [InlineData("session queue move review " + Session + " \"named task\" " + Neighbor + " up")]
    [InlineData("session\tqueue move review " + Pair + " up")]
    [InlineData(Valid + "\n")]
    [InlineData(Valid + ";")]
    public void MalformedReservedInputNeverBecomesASelectorOrConfirmation(string input)
    {
        var parsed = SessionQueueMoveReviewCommand.Parse(input, "Kora");
        parsed.Should().NotBeNull();
        parsed!.Error.Should().Contain(SessionQueueMoveReviewCommand.Syntax).And.Contain("Preparation only");
        parsed.SessionId.Should().BeEmpty();
    }

    [Fact]
    public void Utf8BoundIsExactAndNeverTruncated()
    {
        var exact = Valid.PadRight(SessionCommand.MaximumInputBytes);
        SessionQueueMoveReviewCommand.Parse(exact, "Kora")!.Error.Should().BeNull();
        SessionQueueMoveReviewCommand.Parse(exact + " ", "Kora")!.Error.Should().NotBeNull();
        SessionQueueMoveReviewCommand.Parse(Valid + new string('\u00e9', 512), "Kora")!.Error.Should().NotBeNull();
    }
}
