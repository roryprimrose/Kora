using AwesomeAssertions;
using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class ClaudeRequestGuideTests
{
    [Fact]
    public void Guide_explains_complete_operation_and_does_not_claim_enablement()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "claude-requests", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("**unavailable**").And.Contain("private native UI only")
            .And.Contain("credential/workspace").And.Contain("thinking disabled")
            .And.Contain("original body and original channel").And.Contain("single-use")
            .And.Contain("/v1/messages/count_tokens").And.Contain("**new** review")
            .And.Contain("complete actual UTF-8 JSON body").And.Contain("no affirmative default")
            .And.Contain("ASSISTANT").And.Contain("cannot retract bytes already transmitted")
            .And.Contain("actually joins").And.Contain("No hidden preflight");
    }
}
