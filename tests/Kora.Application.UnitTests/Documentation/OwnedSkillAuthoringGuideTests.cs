using AwesomeAssertions;
using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class OwnedSkillAuthoringGuideTests
{
    [Fact]
    public void EmbeddedGuideExplainsCompletePublicationAndInactiveRecoveryBoundaries()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "owned-skill-authoring", StringComparison.Ordinal));
        guide.Title.Should().Be("Create and use a declarative skill");
        guide.Markdown.Should().Contain("separate native publication confirmation")
            .And.Contain("exactly two files")
            .And.Contain("owned skill reopen daily-review 1.0.0")
            .And.Contain("immutable")
            .And.Contain("not a rollback")
            .And.Contain("enable, select, advertise or invoke")
            .And.Contain("not autosaved")
            .And.Contain("owned skill use daily-review 1.0.0")
            .And.Contain("owned skill invoke")
            .And.Contain("32768-byte")
            .And.Contain("no hosted fallback")
            .And.Contain("two minutes");
        guide.Markdown.Should().Contain("owned skill tool web-page-get")
            .And.Contain("owned skill url: https://example.com/")
            .And.Contain("owned skill review-tool")
            .And.Contain("Exact page approval")
            .And.Contain("saved disabled")
            .And.Contain("implementation only");
        guide.Markdown.Should().Contain("owned skill inputs required-literal")
            .And.Contain("owned skill input topic: <literal>")
            .And.Contain("single-pass")
            .And.Contain("inputs: required-literal")
            .And.Contain("stay literal")
            .And.Contain("separately saved");
        guide.Markdown.Should().Contain("owned skill critique:")
            .And.Contain("owned skill refine")
            .And.Contain("owned skill cancel-refinement")
            .And.Contain("complete current guidance")
            .And.Contain("complete proposed guidance")
            .And.Contain("does not complete the entire Builder")
            .And.Contain("input names cannot be added")
            .And.Contain("until it joins");
    }
}
