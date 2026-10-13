using System.Collections.Immutable;
using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ConversationSearchTests
{
    [Theory]
    [InlineData("café", true)]
    [InlineData("CAFE\u0301 absent", true)]
    [InlineData("cafe", false)]
    [InlineData("caf", false)]
    [InlineData("unavailable", true)]
    [InlineData("UnregisteredModel", true)]
    [InlineData("Succeeded", false)]
    [InlineData("IntentRecorded", false)]
    [InlineData("LocalUi", false)]
    [InlineData("ignored secret", false)]
    [InlineData("' OR 1=1; DROP TABLE messages;", false)]
    public void OnlyOriginalWordsAndTypedHostFieldsMatch(string query, bool expected)
    {
        var search = new ConversationSearch(query);
        search.Matches(Message(), TestContext.Current.CancellationToken).Should().Be(expected);
        search.Digest.Should().Be(new SessionHistorySearch(query).Digest);
    }

    [Fact]
    public void MatchingPreservesOriginalContentAndUsesInvariantNormalizationDeduplication()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("tr-TR");
            var message = Message() with { Body = "  café i\r\n e\u0301🙂  " };
            new ConversationSearch("I I").Matches(message, TestContext.Current.CancellationToken).Should().BeTrue();
            new ConversationSearch("é").Digest.Should().Be(new ConversationSearch("e\u0301 É").Digest);
            message.Body.Should().Be("  café i\r\n e\u0301🙂  ");
            new ConversationSearch("a").Matches(message with { Body = new('a', 65) },
                TestContext.Current.CancellationToken).Should().BeFalse();
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".*")]
    [InlineData("\ud800")]
    public void InvalidQueryNeverMeansAllMessages(string query)
    {
        var act = () => new ConversationSearch(query);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SharedLexicalLimitsAndCancellationRemainAuthoritative()
    {
        _ = new ConversationSearch(new('a', 64));
        foreach (var query in new[] { new string('a', 65), new string(' ', 257),
            string.Join(' ', Enumerable.Range(0, 33)), string.Join(' ', Enumerable.Repeat(new string('界', 64), 3)) })
        {
            var act = () => new ConversationSearch(query);
            act.Should().Throw<ArgumentException>();
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelledMatch = () => new ConversationSearch("absent").Matches(Message(), cancelled.Token);
        cancelledMatch.Should().Throw<OperationCanceledException>();
        new ConversationSearch("IntentRecorded").Matches(Message() with { Revision = 1, Outcome = null, Reason = null },
            TestContext.Current.CancellationToken).Should().BeTrue();
        new ConversationSearch("absent").Matches(Message() with { Revision = 1, Outcome = null, Reason = null },
            TestContext.Current.CancellationToken).Should().BeFalse();
        new ConversationSearch("Interrupted").Matches(Message() with { Outcome = ModelTurnOutcome.Unknown,
            Reason = ModelTurnReason.HostInterrupted }, TestContext.Current.CancellationToken).Should().BeTrue();
    }

    [Fact]
    public void CompleteSerializationIncludesSnapshotCursorOriginalRequestAndCounters()
    {
        var message = Message();
        var cursor = new ConversationSearchCursor(new(message.Request.SessionId, new(1), 2, 1, 4),
            new ConversationSearch("café").Digest);
        var page = new ConversationSearchPage(new(message.Request.SessionId, new(1), true), 4, 2, cursor.QueryDigest,
            ImmutableArray.Create(message), 1, cursor);
        var bytes = ConversationSearchPage.Serialize(page);
        bytes.Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
        page.OmittedMessages.Should().Be(0);
        page.UnavailableMessages.Should().Be(0);
        page.QueryDigest.Should().Be(cursor.QueryDigest);
        page.Next!.Conversation.Should().Be(cursor.Conversation);
        page.Next.QueryDigest.Should().Be(cursor.QueryDigest);
        page.Messages.Single().Request.Should().Be(message.Request);
        page.Messages.Single().Body.Should().Be(message.Body);
    }

    private static ConversationMessage Message() => new(HostRequest.Create(RequestOrigin.LocalUi), new(1), 1, 2,
        DateTimeOffset.UnixEpoch, "café original USER", ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel);
}
