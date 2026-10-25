using AwesomeAssertions;

using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class AssistantSearchDocumentationTests
{
    [Fact]
    public void EmbeddedAssistantSearchDocumentsExplicitRoleCompleteInspectionAndNoImplicitAuthority()
    {
        var pages = new EmbeddedUserDocumentationProvider().GetPages();
        var commands = pages.Single(page => string.Equals(page.Id, "commands", StringComparison.Ordinal)).Markdown;
        commands.Should().Contain("#### Search retained assistant answers explicitly")
            .And.Contain("--role ASSISTANT -- <verbatim query>")
            .And.Contain("All** compatibility")
            .And.Contain("Next keeps the original query, role, source and nonrenewing 60-second expiry")
            .And.Contain("Missing assistant output does not fall back")
            .And.Contain("Nova, conversation search");
        var windows = pages.Single(page => string.Equals(page.Id, "windows-and-tray", StringComparison.Ordinal)).Markdown;
        windows.Should().Contain("### Search retained assistant answers")
            .And.Contain("complete inert saved answer separately from USER and HOST")
            .And.Contain("Details - exact original USER / separate HOST")
            .And.Contain("Cancel conversation search")
            .And.Contain("Clear private conversation search")
            .And.Contain("never resumes work or renews retention");
    }
}
