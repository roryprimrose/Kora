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
            .And.Contain("/v1/messages/count_tokens").And.Contain("Choosing Claude in Settings is the decision to send")
            .And.Contain("without a byte-review window, at every session permission level")
            .And.Contain("security audit record").And.Contain("Selecting a profile is not permission to transmit")
            .And.Contain("ASSISTANT").And.Contain("cannot retract bytes already transmitted")
            .And.Contain("actually joins").And.Contain("No hidden preflight");
        guide.Should().Contain("claude-tools=application.get_version,readiness.get,runtime.get_status")
            .And.Contain("six calls").And.Contain("32768-byte").And.Contain("4096-byte")
            .And.Contain("each is admitted automatically against current authority").And.Contain("mixed prose/tool proposals");
        guide.Should().Contain("## Ordinary Submit with Claude").And.Contain("**Hosted preferred**")
            .And.Contain("Submit does not select Claude");
        guide.Should().Contain("Stream explicitly with Claude").And.Contain("`stream:true`")
            .And.Contain("128 events").And.Contain("JSON depth 24").And.Contain("Bounds are inclusive")
            .And.Contain("buffered approval does not admit streaming").And.Contain("failed disposal quarantines")
            .And.Contain("two-minute deadline").And.Contain("No implicit selection, retry or buffered fallback");
        guide.Should().Contain("Settings-routed Claude answers are buffered in this increment")
            .And.Contain("**Ask Claude with one clarification** button has been removed")
            .And.Contain("An already pending Claude question continues");
    }
}
