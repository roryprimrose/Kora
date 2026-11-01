using AwesomeAssertions;

using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class ReviewedContextGuideTests
{
    [Fact]
    public void EmbeddedMixedContextGuideDescribesCompleteOperationAndUnavailableBoundaries()
    {
        var page = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(item => string.Equals(item.Id, "reviewed-context", StringComparison.Ordinal));
        page.Title.Should().Be("Review history, an instruction and session memories for one answer");
        page.Markdown.Should().Contain("Confirm ONE local-only tool-free answer")
            .And.Contain("32768 UTF-8").And.Contain("16 evidence items")
            .And.Contain("single-use").And.Contain("two minutes").And.Contain("quiescent")
            .And.Contain("device-profile memory").And.Contain("model memory writes");
        page.Markdown.Should().Contain("OVER BUDGET").And.Contain("Omit this whole contribution and review again")
            .And.Contain("fresh complete review").And.Contain("Required contributions cannot be omitted")
            .And.Contain("original two-minute source deadline");
        page.Markdown.Should().Contain("Reply to this answer").And.Contain("Confirm ONE local-only reply")
            .And.Contain("60 seconds").And.Contain("Cancel reply").And.Contain("memory-only composer")
            .And.Contain("no spoken/typed ordinal Reply command").And.Contain("silently sending the follow-up alone");
    }
}
