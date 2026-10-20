using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Conversation;

namespace Kora.Application.UnitTests.Presentation;

public sealed class ComposerDraftSizeFeedbackTests
{
    [Theory]
    [InlineData("", "valid", "empty/whitespace cannot be submitted")]
    [InlineData(" \t\r\n", "valid", "empty/whitespace cannot be submitted")]
    [InlineData("PRIVATE_MARKER", "valid", "valid")]
    [InlineData("\0", "unsupported control or binary content", "unsupported control or binary content")]
    public void FeedbackContainsOnlyMetricsAndTruthfulShape(string body, string save, string submit)
    {
        var result = ComposerDraftSizeFeedback.Format(ConversationText.Measure(body));
        result.Should().Contain("Save shape: " + save).And.Contain("Submit shape: " + submit)
            .And.Contain("16384 UTF-8 bytes / 4096 UTF-16 code units each")
            .And.Contain("no scalar limit").And.Contain("credential checks");
        result.Should().NotContain("PRIVATE_MARKER");
    }

    [Fact]
    public void MalformedOversizedAndUnavailableFeedbackIsNotSuccessShaped()
    {
        ComposerDraftSizeFeedback.Format(ConversationText.Measure("\ud800")).Should()
            .Contain("Malformed Unicode").And.Contain("count unavailable")
            .And.Contain("Save shape: ill-formed Unicode; Submit shape: ill-formed Unicode");
        ComposerDraftSizeFeedback.Format(ConversationText.Measure(new string('a', 4097))).Should()
            .Contain("4097 UTF-8 bytes; 4097 Unicode scalars").And.Contain("Save shape: over limit; Submit shape: over limit");
        ComposerDraftSizeFeedback.Format(null).Should().Contain("metrics unavailable").And.NotContain("0 UTF");
    }
}
