using AwesomeAssertions;

using Kora.Application.Documentation;

namespace Kora.Application.UnitTests.Documentation;

public sealed class SessionDictationGuideTests
{
    [Fact]
    public void GuideExplainsUnavailableLiteralCaptureAndCompleteMemoryOnlyInteraction()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "session-dictation", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("**unavailable in production Windows capture**")
            .And.Contain("**memory-only**")
            .And.Contain("**Active**")
            .And.Contain("**Dictate session message...**")
            .And.Contain("actual microphone release")
            .And.Contain("**Append**, **Replace** or **Cancel dictation**")
            .And.Contain("without adding a space or newline")
            .And.Contain("**Submit** action separately")
            .And.Contain("inert text, not commands or approval")
            .And.Contain("independent unsent input")
            .And.Contain("does not introduce disk autosave");
        guide.Should().Contain("Provisional—may change").And.Contain("512 UTF-16")
            .And.Contain("1,024 strict UTF-8").And.Contain("Final-only capture")
            .And.Contain("not inserted, sent or spoken automatically");
    }
}
