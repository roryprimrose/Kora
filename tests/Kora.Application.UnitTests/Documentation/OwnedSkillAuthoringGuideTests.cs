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
        guide.Markdown.Should().Contain("Start SIMULATED test")
            .And.Contain("owned skill fixture:")
            .And.Contain("owned skill cancel-test")
            .And.Contain("8192 UTF-8 bytes")
            .And.Contain("Unknown/duplicate fields")
            .And.Contain("not a real observation")
            .And.Contain("unchanged skill format");
        guide.Markdown.Should().Contain("owned skill workflow create inspection 1.0.0")
            .And.Contain("Save disabled revision")
            .And.Contain("owned skill workflow approve-step")
            .And.Contain("8192 complete labelled result bytes")
            .And.Contain("15-second total budget")
            .And.Contain("history is neither read nor written")
            .And.Contain("no automatic pagination");
        guide.Markdown.Should().Contain("create a skill that explains a deployment failure")
            .And.Contain("owned skill expected-input:")
            .And.Contain("owned skill expected-output:")
            .And.Contain("owned skill generate")
            .And.Contain("complete generated manifest and guidance")
            .And.Contain("no-overwrite")
            .And.Contain("unqualified");
        guide.Markdown.Should().Contain("owned skill delete daily-review 1.0.0")
            .And.Contain("Delete only this exact owned source")
            .And.Contain("does not physically cancel immutable admitted tasks")
            .And.Contain("absence readback")
            .And.Contain("do not retry or recreate");
    }
}
