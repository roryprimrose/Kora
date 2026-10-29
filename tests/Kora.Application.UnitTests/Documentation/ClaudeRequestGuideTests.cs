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
        guide.Should().Contain("claude-tools=application.get_version,readiness.get,runtime.get_status")
            .And.Contain("six calls").And.Contain("32768-byte").And.Contain("4096-byte")
            .And.Contain("**each needs fresh native consent**").And.Contain("mixed prose/tool proposals");
        guide.Should().Contain("Stream explicitly with Claude").And.Contain("`stream:true`")
            .And.Contain("128 events").And.Contain("JSON depth 24").And.Contain("Bounds are inclusive")
            .And.Contain("buffered approval does not admit streaming").And.Contain("failed disposal quarantines")
            .And.Contain("two-minute deadline").And.Contain("No implicit selection, retry or buffered fallback");
        guide.Should().Contain("Ask Claude with one clarification").And.Contain("**Answering does not send your answer.**")
            .And.Contain("canonical stored reply").And.Contain("Each physical POST needs fresh affirmative native consent")
            .And.Contain("Copilot/Codex questions");
    }
}
