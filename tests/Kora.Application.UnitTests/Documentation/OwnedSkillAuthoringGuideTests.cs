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
        guide.Title.Should().Be("Create an instruction-only skill");
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
    }
}
