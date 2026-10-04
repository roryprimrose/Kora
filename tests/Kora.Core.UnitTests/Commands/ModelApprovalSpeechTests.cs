using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class ModelApprovalSpeechTests
{
    [Theory]
    [InlineData("Nova, yes", true, ModelApprovalReply.Once)]
    [InlineData("Nova approve for this session", true, ModelApprovalReply.Session)]
    [InlineData("Nova, always allow this", true, ModelApprovalReply.Always)]
    [InlineData("Nova, no", true, ModelApprovalReply.Reject)]
    [InlineData("always allow this", false, ModelApprovalReply.Always)]
    public void Matches_exact_approval_phrases(
        string transcript,
        bool requireName,
        ModelApprovalReply expected)
    {
        ModelApprovalSpeech.TryMatch(transcript, "Nova", requireName, out var reply)
            .Should().BeTrue();
        reply.Should().Be(expected);
    }

    [Theory]
    [InlineData("always allow this", true)]
    [InlineData("Nova, always allow the computer to lock", true)]
    [InlineData("Nova, yes, even tomorrow", true)]
    [InlineData("someone said Nova approve", true)]
    public void Rejects_missing_activation_or_extra_words(string transcript, bool requireName)
    {
        ModelApprovalSpeech.TryMatch(transcript, "Nova", requireName, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Speech_grammar_contains_name_prefixed_and_bare_replies()
    {
        var phrases = ModelApprovalSpeech.GetPhrases("Nova Prime").ToArray();

        phrases.Should().Contain("Nova Prime approve for this session");
        phrases.Should().Contain("always allow this");
    }
}
