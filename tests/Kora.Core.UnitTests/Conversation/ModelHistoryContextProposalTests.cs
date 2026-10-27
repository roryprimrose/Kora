using AwesomeAssertions;

using Kora.Core.Conversation;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ModelHistoryContextProposalTests
{
    [Theory]
    [InlineData("{\"version\":1,\"advisory\":\"HISTORYREVIEW\"}")]
    [InlineData(" \r\n{ \"advisory\" : \"HISTORYREVIEW\", \"version\" : 1 }\t")]
    public void OnlyTheFixedVersionedInertAdvisoryIsRecognized(string text) =>
        ModelHistoryContextProposal.IsAdvisory(text).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("answer")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("{}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"advisory\":\"HISTORYREVIEW\"}")]
    [InlineData("{\"version\":2,\"advisory\":\"HISTORYREVIEW\"}")]
    [InlineData("{\"version\":1.5,\"advisory\":\"HISTORYREVIEW\"}")]
    [InlineData("{\"version\":2147483648,\"advisory\":\"HISTORYREVIEW\"}")]
    [InlineData("{\"version\":\"1\",\"advisory\":\"HISTORYREVIEW\"}")]
    [InlineData("{\"version\":null,\"advisory\":\"HISTORYREVIEW\"}")]
    [InlineData("{\"version\":1,\"advisory\":null}")]
    [InlineData("{\"version\":1,\"advisory\":true}")]
    [InlineData("{\"version\":1,\"advisory\":\"historyreview\"}")]
    [InlineData("{\"version\":1,\"Advisory\":\"HISTORYREVIEW\"}")]
    [InlineData("{\"version\":1,\"advisory\":\"HISTORYREVIEW\",\"version\":1}")]
    [InlineData("{\"version\":1,\"advisory\":\"HISTORYREVIEW\",\"advisory\":\"HISTORYREVIEW\"}")]
    [InlineData("{\"version\":1,\"advisory\":\"HISTORYREVIEW\",\"messageIds\":[]}")]
    [InlineData("{\"version\":1,\"advisory\":\"HISTORYREVIEW\",\"question\":\"replacement\"}")]
    [InlineData("{\"version\":1,\"advisory\":\"HISTORYREVIEW\",\"approved\":true}")]
    [InlineData("{\"version\":1,\"advisory\":\"HISTORYREVIEW\",\"sessionId\":\"chosen\"}")]
    [InlineData("{\"version\":1,\"advisory\":{}}")]
    [InlineData("{\"version\":1,\"advisory\":\"HISTORYREVIEW\",}")]
    [InlineData("{\"version\":1,\"advisory\":\"HISTORYREVIEW\"} trailing")]
    [InlineData("```json\n{\"version\":1,\"advisory\":\"HISTORYREVIEW\"}\n```")]
    public void MalformedDuplicateUnknownOrAuthorityFieldsAreNotAdvisories(string text) =>
        ModelHistoryContextProposal.IsAdvisory(text).Should().BeFalse();

    [Fact]
    public void CompleteUtf8BoundAndInvalidEncodingAreRejectedWithoutTruncation()
    {
        ModelHistoryContextProposal.IsAdvisory(new string(' ', 129)).Should().BeFalse();
        ModelHistoryContextProposal.IsAdvisory(new string('\u00e9', 65)).Should().BeFalse();
        ModelHistoryContextProposal.IsAdvisory("\ud800").Should().BeFalse();
        var exact = "{\"version\":1,\"advisory\":\"HISTORYREVIEW\"}";
        ModelHistoryContextProposal.IsAdvisory(exact.PadRight(128)).Should().BeTrue();
        ModelHistoryContextProposal.IsAdvisory(exact.PadRight(129)).Should().BeFalse();
        ModelHistoryContextProposal.ModelPolicy.Should().Contain(exact).And.Contain("unchanged original question");
        var missing = () => ModelHistoryContextProposal.IsAdvisory(null!);
        missing.Should().Throw<ArgumentNullException>();
    }
}
