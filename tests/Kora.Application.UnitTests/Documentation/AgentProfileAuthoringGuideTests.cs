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
        var observation = example.Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
            .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal)
            .Replace("maximum-rounds: 1", "maximum-rounds: 2", StringComparison.Ordinal);
        var observationProfile = OwnedAgentProfileDocument.Parse("example", observation).Profile;
        observationProfile.Tools.Should().Equal("application.get_version");
        observationProfile.References.Should().BeEmpty();
        observationProfile.MaximumCalls.Should().Be(1);
        observationProfile.MaximumRounds.Should().Be(2);
        guide.Should().Contain("Manual v1 authoring").And.Contain("positive call budget")
            .And.Contain("refuses observation-capable drafts before any model call");
        guide.Should().Contain("complete proposed").And.Contain("separate native")
            .And.Contain("Save / Discard / Cancel").And.Contain("not success or rollback")
            .And.Contain("read-only").And.Contain("does not\nselect, enable or invoke")
            .And.Contain("original typed/activated-voice").And.Contain("ignores cancellation");
        var combined = guide.Split("```markdown\n", StringSplitOptions.None)[2].Split("```", StringSplitOptions.None)[0];
        var profile = OwnedAgentProfileDocument.Parse("example", combined).Profile;
        profile.Tools.Should().Equal("application.get_version");
        profile.References.Single().Id.Should().Be("disk.kora-profile.instruction.example");
        guide.Should().Contain("v2/v3 authoring").And.Contain("not resolution consent or enablement");
        guide.Should().Contain("agent author draft example:").And.Contain("Use proposed draft")
            .And.Contain("agent author describe").And.Contain("agent author ask")
            .And.Contain("two-minute").And.Contain("not sent to the model");
        guide.Should().Contain("Edit existing original").And.Contain("Save existing original")
            .And.Contain("Overwrite with Kora changes").And.Contain("Cancel (keep draft)")
            .And.Contain("not compare-and-swap").And.Contain("Missing originals cannot be recreated");
    }
}
