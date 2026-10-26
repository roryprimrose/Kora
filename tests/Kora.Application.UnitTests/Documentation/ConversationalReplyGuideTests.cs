using AwesomeAssertions;
using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class ConversationalReplyGuideTests
{
    [Fact]
    public void SettingsGuideExplainsOptInExactPlaybackOneShotAndNativeRecoveryWithoutQualificationClaims()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "settings", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("## Conversational voice replies")
            .And.Contain("**Enable conversational replies**")
            .And.Contain("**Turn off**")
            .And.Contain("**15 seconds to start**")
            .And.Contain("**60 seconds of speech**")
            .And.Contain("**Cancel reply**")
            .And.Contain("Approval and transmission-consent dialogs never open automatic capture")
            .And.Contain("without retry or rearm")
            .And.Contain("**Answer by voice**")
            .And.Contain("Merely displaying a card does not start transcription")
            .And.Contain("does not enable ambient name listening")
            .And.Contain("unqualified hardware/acoustic profiles");
    }
}
