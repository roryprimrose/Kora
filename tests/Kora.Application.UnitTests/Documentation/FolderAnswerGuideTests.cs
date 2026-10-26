using AwesomeAssertions;
using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class FolderAnswerGuideTests
{
    [Fact]
    public void GuideExplainsCompleteReviewedOperationAndUnavailableEnablementWithoutRegistrationDependency()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "folder-answers", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("32 immediate files").And.Contain("1 MiB").And.Contain("256 KiB")
            .And.Contain("Review folder excerpts for this question")
            .And.Contain("Confirm: answer locally using only these reviewed folder excerpts")
            .And.Contain("Show exact folder answer citation")
            .And.Contain("32,768-byte").And.Contain("eight excerpts").And.Contain("16 KiB")
            .And.Contain("composed source-use policy currently remains unavailable")
            .And.Contain("no model invocation").And.Contain("Citation identity is verified, not semantic truth")
            .And.Contain("immutable, volatile snapshot").And.Contain("not a named knowledge source")
            .And.Contain("actual provider work and asynchronous lease disposal")
            .And.Contain("No tools, hosted fallback").And.Contain("no model invocation");
    }
}
