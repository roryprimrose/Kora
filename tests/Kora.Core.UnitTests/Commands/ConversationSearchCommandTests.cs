using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Commands;

public sealed class ConversationSearchCommandTests
{
    private const string Id = "abcdef12-1234-1234-1234-123456789012";

    [Theory]
    [InlineData("conversation search " + Id + " -- ", "  MiXeD e\u0301 café  ")]
    [InlineData("Nova, CONVERSATION SEARCH " + Id + " -- ", "\"quoted\" -- literal")]
    [InlineData("Nova conversation search " + Id + " -- ", "a\tb\r\nUnicode 🙂")]
    public void SearchPreservesEverythingAfterExplicitBoundary(string header, string query)
    {
        var command = SessionCommand.Parse(header + query, "Nova")!;
        command.Operation.Should().Be(SessionCommandOperation.ConversationSearch);
        command.IsConversationSearch.Should().BeTrue();
        command.SessionId.Should().Be(Guid.Parse(Id));
        command.ConversationQuery.Should().Be(query);
        new ConversationSearch(command.ConversationQuery!).Digest.Should().Be(new ConversationSearch(query).Digest);
    }

    [Theory]
    [InlineData("conversation")]
    [InlineData("conversation search")]
    [InlineData("conversation browse " + Id)]
    [InlineData("conversation search " + Id)]
    [InlineData("conversation search " + Id + " --")]
    [InlineData("conversation search " + Id + " -- ")]
    [InlineData("conversation search " + Id + " --  \t\r\n")]
    [InlineData("conversation search " + Id + " -- ***")]
    [InlineData("conversation search " + Id + " -- \ud800")]
    [InlineData("conversation search selected -- word")]
    [InlineData("conversation search ABCDEF12-1234-1234-1234-123456789012 -- word")]
    [InlineData("conversation search {abcdef12-1234-1234-1234-123456789012} -- word")]
    [InlineData("conversation search 00000000-0000-0000-0000-000000000000 -- word")]
    [InlineData("conversation search " + Id + " limit 1 -- word")]
    [InlineData("conversation search " + Id + " after {} -- word")]
    [InlineData("conversation\tsearch " + Id + " -- word")]
    [InlineData("conversation search next selected")]
    [InlineData("conversation search next ABCDEF12-1234-1234-1234-123456789012")]
    [InlineData("conversation search next " + Id + " {}")]
    [InlineData("conversation search cancel 00000000-0000-0000-0000-000000000000")]
    [InlineData("conversation search cancel " + Id + " -- query")]
    [InlineData("conversation search unknown " + Id)]
    public void InvalidSearchIsClaimedWithoutBrowseOrQuestionFallback(string input)
    {
        var command = SessionCommand.Parse(input, "Nova")!;
        command.Operation.Should().Be(SessionCommandOperation.Invalid);
        command.IsConversationSearch.Should().BeFalse();
        command.Error.Should().Be(SessionCommand.ConversationSearchSyntax);
    }

    [Theory]
    [InlineData("next", SessionCommandOperation.ConversationSearchNext)]
    [InlineData("CANCEL", SessionCommandOperation.ConversationSearchCancel)]
    public void ContinuationAcceptsOnlyExactAddressNotCallerCursor(string verb, SessionCommandOperation expected)
    {
        var command = SessionCommand.Parse("conversation search " + verb + " " + Id, "Kora")!;
        command.Operation.Should().Be(expected);
        command.SessionId.Should().Be(Guid.Parse(Id));
        command.ConversationQuery.Should().BeNull();
        command.IsConversationSearch.Should().BeTrue();
    }

    [Fact]
    public void CanonicalLimitsAndUnrelatedReceiptGrammarRemainAuthoritative()
    {
        OptionalCanonicalGuid.ParseRequired(Id).Should().Be(Guid.Parse(Id));
        var missing = () => OptionalCanonicalGuid.ParseRequired(string.Empty);
        missing.Should().Throw<InvalidDataException>();
        var header = "conversation search " + Id + " -- ";
        SessionCommand.Parse(header + new string('a', 65), "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse(header + string.Join(' ', Enumerable.Repeat("word", 60)), "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse(header + new string('\u0800', 172), "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse(new string(' ', SessionCommand.MaximumInputBytes) + header + "word", "Kora")!.ConversationQuery.Should().Be("word");
        SessionCommand.Parse("conversation " + new string(' ', SessionCommand.MaximumInputBytes) + "search " + Id + " -- word", "Kora")!
            .Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("session search " + Id + " \"word\"", "Kora")!.Operation.Should().Be(SessionCommandOperation.HistorySearch);
        SessionCommand.Parse("conversationx search " + Id + " -- word", "Kora").Should().BeNull();
    }
}
