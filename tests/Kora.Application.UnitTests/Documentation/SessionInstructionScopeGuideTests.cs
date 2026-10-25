using AwesomeAssertions;

using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class SessionInstructionScopeGuideTests
{
    [Fact]
    public void EmbeddedGuideExplainsCompleteOperationAndExclusions()
    {
        var page = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(item => string.Equals(item.Id, "session-instructions", StringComparison.Ordinal));
        page.Title.Should().Be("Apply an instruction to this Active session");
        page.Markdown.Should().Contain("Send two ordinary requests")
            .And.Contain("Confirm apply to this session").And.Contain("Stop applying")
            .And.Contain("no hosted fallback").And.Contain("two minutes").And.Contain("No spoken GUID")
            .And.Contain("questions and their continuations").And.Contain("not saved");
    }
}
