using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(ConversationSearchRole.All)]
    [InlineData(ConversationSearchRole.USER)]
    [InlineData(ConversationSearchRole.HOST)]
    public async Task OriginalSequenceAndTimeRangesComposeAcrossBoundedPages(ConversationSearchRole role)
    {
        using var f = new ConversationFixture();
        f.Messages = Enumerable.Range(1, ConversationText.MaximumSessionMessages).Select(index =>
            Message(f, index, "marker") with { SubmittedAt = DateTimeOffset.UnixEpoch.AddTicks(index) }).ToArray();
        var range = new ConversationRangeFilter(2, 99, f.Messages[2].SubmittedAt, f.Messages[97].SubmittedAt);
        var terminal = new ConversationTerminalFilter(ModelTurnOutcome.Unavailable);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var results = new List<ConversationMessage>();
        ConversationSearchCursor? cursor = null;
        var scanned = 0;
        do
        {
            var page = await f.Service.SearchConversationAsync(f.Session.SessionId, "marker Unavailable", cursor, 25,
                f.Token, role, terminal, range);
            page.Range.Should().Be(range);
            page.Scanned.Should().BeInRange(1, ConversationText.MaximumSessionMessages);
            ConversationSearchPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
            scanned += page.Scanned;
            results.AddRange(page.Messages);
            cursor = page.Next;
            if (cursor is not null) { cursor.Range.Should().Be(range); }
        } while (cursor is not null);
        scanned.Should().Be(100);
        results.Should().Equal(role is ConversationSearchRole.USER ? [] : f.Messages.Skip(2).Take(96));
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Fact]
    public async Task EveryRangeBoundAndDigestIsBoundToContinuationBeforeReading()
    {
        using var f = new ConversationFixture();
        f.Messages = [Message(f, 1, "marker"), Message(f, 2, "marker")];
        var range = new ConversationRangeFilter(1, 2, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var first = await f.Service.SearchConversationAsync(f.Session.SessionId, "marker", null, 1, f.Token, range: range);
        var reads = f.Reads;
        foreach (var changed in new[] { range with { MinimumSequence = 2 }, range with { MaximumSequence = 1 },
            range with { SubmittedFrom = DateTimeOffset.UnixEpoch }, range with { SubmittedThrough = DateTimeOffset.UnixEpoch }, default })
        {
            await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId,
                "marker", first.Next, 1, f.Token, range: changed)).Should().ThrowAsync<ArgumentException>();
            await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId,
                "marker", first.Next! with { Range = changed }, 1, f.Token, range: range)).Should().ThrowAsync<ArgumentException>();
        }
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId,
            "marker", first.Next! with { QueryDigest = "wrong" }, 1, f.Token, range: range)).Should().ThrowAsync<ArgumentException>();
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId,
            "marker", null, 1, f.Token, range: new(0))).Should().ThrowAsync<ArgumentException>();
        f.Reads.Should().Be(reads);
        var next = await f.Service.SearchConversationAsync(f.Session.SessionId, "MARKER", first.Next, 1, f.Token, range: range);
        next.Messages.Should().Equal(f.Messages[1]);
        next.Next.Should().BeNull();
    }

    [Theory]
    [InlineData("body")]
    [InlineData("time")]
    [InlineData("status")]
    [InlineData("owner")]
    public async Task RangeExcludedRowsCannotConcealInvalidSource(string invalid)
    {
        using var f = new ConversationFixture();
        var message = Message(f, 1, "marker");
        f.Messages = [invalid switch
        {
            "body" => message with { Body = "\ud800" },
            "time" => message with { SubmittedAt = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)) },
            "status" => message with { Reason = (ModelTurnReason)999 },
            _ => message with { Request = Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi) },
        }];
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId,
            "absent", null, 25, f.Token, range: new(99))).Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
    }
}
