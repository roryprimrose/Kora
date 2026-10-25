using AwesomeAssertions;
using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class AgentSharedSkillGuideTests
{
    [Fact]
    public void EmbeddedGuideDescribesTheWholeOperationAndSeparateAuthorityStages()
    {
        var page = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(item => string.Equals(item.Id, "agent-shared-skill-guidance", StringComparison.Ordinal));
        page.Title.Should().Be("Use a shared skill as guidance for one local agent task");
        page.Markdown.Should().Contain("Consent to read declared skill Markdown")
            .And.Contain("Enable this complete skill review once")
            .And.Contain("Consent to this exact bounded invocation")
            .And.Contain("LocalOnly")
            .And.Contain("until completion joins")
            .And.Contain("Hosts without the shared-skill resolver explicitly refuse format 3")
            .And.Contain("agent skill select");
    }
}
