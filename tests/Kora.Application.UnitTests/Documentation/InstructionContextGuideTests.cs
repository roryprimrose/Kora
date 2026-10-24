using AwesomeAssertions;

using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class InstructionContextGuideTests
{
    [Fact]
    public void EmbeddedInstructionGuideHasNavigableTitleAndExplicitOneShotReviewAndLocalOnlyBoundaries()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "conversation-instructions", StringComparison.Ordinal));
        guide.Title.Should().Be("Use an owned instruction for one conversation request");
        guide.Markdown.Should().Contain("Submit this one reviewed local request")
            .And.Contain("complete pinned native source")
            .And.Contain("lower-trust guidance under host policy")
            .And.Contain("no tools, hosted transfer")
            .And.Contain("two minutes")
            .And.Contain("ordinary **Submit exact message** action ignores");
    }
}
