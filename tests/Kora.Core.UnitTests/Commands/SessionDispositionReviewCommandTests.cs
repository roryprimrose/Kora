using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionDispositionReviewCommandTests
{
    private const string Id = "12345678-1234-1234-abcd-123456789012";
    private const string Valid = "review session disposition " + Id + " 1 0";

    [Theory]
    [InlineData(Valid, "Kora", 1, 0)]
    [InlineData("Nova, " + Valid, "Nova", 1, 0)]
    [InlineData("nova " + Valid, "Nova", 1, 0)]
    [InlineData("  REVIEW  SESSION DISPOSITION " + Id + " 9223372036854775807 9223372036854775807 ", "Kora", long.MaxValue, long.MaxValue)]
    public void ParseBindsOnlyTheExplicitCanonicalIdentityAndObservedRevisions(string input, string name, long generation, long revision)
    {
        SessionDispositionReviewCommand.Parse(input, name).Should()
            .Be(new SessionDispositionReviewCommand(Guid.Parse(Id), generation, revision));
    }

    [Theory]
    [InlineData("")]
    [InlineData("review")]
    [InlineData("Kora")]
    [InlineData("KoraX, " + Valid)]
    [InlineData("Kora:" + Valid)]
    [InlineData("Kora, " + Valid)]
    [InlineData("session delete selected")]
    [InlineData("review session retention")]
    [InlineData("confirm session retention")]
    [InlineData("review other disposition")]
    [InlineData("open session disposition")]
    [InlineData("review session dispositionX")]
    public void ParseDoesNotClaimUnrelatedNamespacesOrAnOldAssistantName(string input)
    {
        SessionDispositionReviewCommand.Parse(input, "Nova").Should().BeNull();
    }

    [Theory]
    [InlineData("review session")]
    [InlineData("review session disposition")]
    [InlineData("review session disposition selected 1 0")]
    [InlineData("review session disposition current 1 0")]
    [InlineData("review session disposition \"a name\" 1 0")]
    [InlineData("review session disposition 00000000-0000-0000-0000-000000000000 1 0")]
    [InlineData("review session disposition 1234567812341234abcd123456789012 1 0")]
    [InlineData("review session disposition {12345678-1234-1234-abcd-123456789012} 1 0")]
    [InlineData("review session disposition 12345678-1234-1234-ABCD-123456789012 1 0")]
    [InlineData("review session disposition " + Id + " 0 0")]
    [InlineData("review session disposition " + Id + " -1 0")]
    [InlineData("review session disposition " + Id + " +1 0")]
    [InlineData("review session disposition " + Id + " 9223372036854775808 0")]
    [InlineData("review session disposition " + Id + " 1 -1")]
    [InlineData("review session disposition " + Id + " 1 +0")]
    [InlineData("review session disposition " + Id + " 1 9223372036854775808")]
    [InlineData("review session disposition " + Id + " 1 0 \"confirm\"")]
    [InlineData("review session disposition " + Id + " 1 0;confirm")]
    [InlineData("review session disposition " + Id + " 1 0\n")]
    [InlineData("review\tsession disposition " + Id + " 1 0")]
    [InlineData("confirm session disposition")]
    [InlineData("confirm session disposition " + Id + " 1 0")]
    public void ParseReservesMalformedAndConfirmationInputWithoutCreatingAConfirmationRoute(string input)
    {
        var result = SessionDispositionReviewCommand.Parse(input, "Kora");
        result.Should().NotBeNull();
        result!.Error.Should().Contain(SessionDispositionReviewCommand.Syntax).And.Contain("typed/voice confirmation is unavailable");
        result.SessionId.Should().BeEmpty();
    }

    [Fact]
    public void ParseRejectsTheExactUtf8LimitWithoutTruncatingOrRepairing()
    {
        var padded = Valid.PadRight(SessionCommand.MaximumInputBytes);
        SessionDispositionReviewCommand.Parse(padded, "Kora")!.Error.Should().BeNull();
        SessionDispositionReviewCommand.Parse(padded + " ", "Kora")!.Error.Should().NotBeNull();
        var prefix = Valid + " ";
        var exact = prefix + new string('x', SessionCommand.MaximumInputBytes - prefix.Length);
        SessionDispositionReviewCommand.Parse(exact, "Kora")!.Error.Should().NotBeNull();
        SessionDispositionReviewCommand.Parse(exact + "x", "Kora")!.Error.Should().NotBeNull();
        SessionDispositionReviewCommand.Parse(prefix + new string('\u00e9', 512), "Kora")!.Error.Should().NotBeNull();
    }
}
