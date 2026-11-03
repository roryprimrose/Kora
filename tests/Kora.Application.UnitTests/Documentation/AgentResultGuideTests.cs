using AwesomeAssertions;

using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class AgentResultGuideTests
{
    [Fact]
    public void AgentResultGuideIsEmbeddedLinkedAndSeparatesObservationFromRecoveryAuthority()
    {
        var pages = new EmbeddedUserDocumentationProvider().GetPages();
        pages.Single(page => string.Equals(page.Id, "readme", StringComparison.Ordinal)).Markdown
            .Should().Contain("(agent-results.md)");
        var guide = pages.Single(page => string.Equals(page.Id, "agent-results", StringComparison.Ordinal));
        guide.Title.Should().Be("Inspect and recover a governed agent result");
        guide.Markdown.Should().Contain("open observed result <presented name>")
            .And.Contain("ToolLoopQualified=false")
            .And.Contain("No assistant answer was produced")
            .And.Contain("separately confirm once")
            .And.Contain("does not rewrite the original failed task")
            .And.Contain("Legacy agent answers without retained callback items");
    }
}
