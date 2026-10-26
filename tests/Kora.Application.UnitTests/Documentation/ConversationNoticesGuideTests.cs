using AwesomeAssertions;

using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class ConversationNoticesGuideTests
{
    [Fact]
    public void GuidePreservesExactInspectionAndUnavailableEffects()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "conversation-notices", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("already-open").And.Contain("**Review**").And.Contain("**Dismiss**").And.Contain("**Defer**")
            .And.Contain("cannot answer, approve, retry, dispatch, speak, open a window or take focus")
            .And.Contain("thirty minutes after the original submission").And.Contain("Unknown is never inferred success")
            .And.Contain("Refresh never renews activity or authority").And.Contain("general reminder scheduler");
    }
}
