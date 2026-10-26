using AwesomeAssertions;

using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class NamedAgentGuideTests
{
    [Fact]
    public void EmbeddedGuideDescribesCompletionRatherThanSourcePreparation()
    {
        var pages = new EmbeddedUserDocumentationProvider().GetPages();
        var commands = pages.Single(item => string.Equals(item.Id, "commands", StringComparison.Ordinal)).Markdown;
        commands.Should().Contain("agent invoke \"Exact readable name\" <original task>")
            .And.Contain("agent conversation <displayed conversation name>")
            .And.Contain("agent confirm invocation").And.Contain("joins owned work");
        var guide = pages.Single(item => string.Equals(item.Id, "tools-and-built-in-skills", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("opening or populating the window is not a completed invocation")
            .And.Contain("retains the actual answer or truthful terminal in exactly that")
            .And.Contain("does not relabel").And.Contain("only after quiescence");
    }
}
