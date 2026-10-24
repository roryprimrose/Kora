using AwesomeAssertions;

using Kora.Application.Documentation;
using Kora.Core.Agents;

namespace Kora.Application.UnitTests.Documentation;

public sealed class AgentProfileAuthoringGuideTests
{
    [Fact]
    public void EmbeddedGuideContainsARealV1OriginalAndExactNativeOnlyCompletion()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages().Single(page =>
            string.Equals(page.Id, "agent-profile-authoring", StringComparison.Ordinal)).Markdown.ReplaceLineEndings("\n");
        var example = guide.Split("```markdown\n", StringSplitOptions.None)[1].Split("```", StringSplitOptions.None)[0];
        OwnedAgentProfileDocument.Parse("example", example).Profile.Tools.Should().BeEmpty();
        guide.Should().Contain("complete proposed").And.Contain("separate native")
            .And.Contain("Save / Discard / Cancel").And.Contain("not success or rollback")
            .And.Contain("read-only").And.Contain("does not\nselect, enable or invoke")
            .And.Contain("original typed/activated-voice").And.Contain("ignores cancellation");
    }
}
