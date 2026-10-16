using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(ConversationSearchRole.All)]
    [InlineData(ConversationSearchRole.USER)]
    [InlineData(ConversationSearchRole.HOST)]
    public async Task CorrelationComposesWithRangesAndTerminalAcrossBoundedOriginalPages(ConversationSearchRole role)
    {
        using var f = new ConversationFixture();
        var original = Message(f, 1, "marker Denied\r\n e\u0301");
        f.Messages = Enumerable.Range(1, 100).Select(index => original with
        {
            Sequence = index, SubmittedAt = DateTimeOffset.UnixEpoch.AddTicks(index),
            Request = index % 2 == 0 ? original.Request : Message(f, index, "other").Request,
        }).ToArray();
        var correlation = new ConversationCorrelationFilter(original.Request.RequestId, original.Request.TaskId);
        var range = new ConversationRangeFilter(2, 98, f.Messages[1].SubmittedAt, f.Messages[97].SubmittedAt);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var records = new List<ConversationMessage>();
        ConversationSearchCursor? cursor = null;
        var scanned = 0;
        do
        {
            var page = await f.Service.SearchConversationAsync(f.Session.SessionId, "marker Unavailable", cursor, 25,
                f.Token, role, new(ModelTurnOutcome.Unavailable), range, correlation);
            page.Correlation.Should().Be(correlation);
            ConversationSearchPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
            scanned += page.Scanned;
            records.AddRange(page.Messages);
            cursor = page.Next;
            if (cursor is not null) { cursor.Correlation.Should().Be(correlation); }
        } while (cursor is not null);
        records.Should().Equal(role is ConversationSearchRole.USER ? [] : f.Messages.Where(row => row.Sequence is >= 2 and <= 98 && row.Sequence % 2 == 0));
        scanned.Should().Be(100);
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Fact]
    public async Task CorrelationAndDigestMismatchOrInvalidPredicateRefusesContinuationBeforeReading()
    {
        using var f = new ConversationFixture();
        var original = Message(f, 1, "marker");
        f.Messages = [original, original with { Sequence = 2 }];
        var correlation = new ConversationCorrelationFilter(original.Request.RequestId, original.Request.TaskId);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var first = await f.Service.SearchConversationAsync(f.Session.SessionId, "marker", null, 1, f.Token, correlation: correlation);
        var reads = f.Reads;
        foreach (var changed in new[] { default, correlation with { RequestId = null }, correlation with { TaskId = null },
            correlation with { RequestId = new(Guid.NewGuid()) }, correlation with { TaskId = new(Guid.NewGuid()) } })
        {
            await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId, "marker",
                first.Next, 1, f.Token, correlation: changed)).Should().ThrowAsync<ArgumentException>();
            await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId, "marker",
                first.Next! with { Correlation = changed }, 1, f.Token, correlation: correlation)).Should().ThrowAsync<ArgumentException>();
        }
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId, "marker",
            first.Next! with { QueryDigest = "wrong" }, 1, f.Token, correlation: correlation)).Should().ThrowAsync<ArgumentException>();
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId, "marker",
            null, 1, f.Token, correlation: new(default(HostId<RequestIdentity>)))).Should().ThrowAsync<ArgumentException>();
        f.Reads.Should().Be(reads);
        var next = await f.Service.SearchConversationAsync(f.Session.SessionId, "MARKER", first.Next, 1, f.Token, correlation: correlation);
        next.Messages.Should().Equal(f.Messages[1]);
        next.Next.Should().BeNull();
        var absent = await f.Service.SearchConversationAsync(f.Session.SessionId, "marker", null, 25, f.Token,
            correlation: new(new(Guid.NewGuid()), new(Guid.NewGuid())));
        absent.Messages.Should().BeEmpty();
        absent.Scanned.Should().Be(2);
        absent.Session.Should().Be(f.Session);
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("request")]
    [InlineData("body")]
    [InlineData("owner")]
    [InlineData("origin")]
    [InlineData("invocation")]
    public async Task CorrelationNonmatchesCannotHideInvalidMessagesOrRequests(string invalid)
    {
        using var f = new ConversationFixture();
        var row = Message(f, 1, "marker");
        f.Messages = [invalid switch
        {
            "request" => row with { Request = null! },
            "body" => row with { Body = "\ud800" },
            "owner" => row with { Request = HostRequest.Create(RequestOrigin.LocalUi) },
            "origin" => row with { Request = new(row.Request.RequestId, f.Session.SessionId, row.Request.TaskId, RequestOrigin.HostSystem) },
            _ => row with { Request = new(row.Request.RequestId, f.Session.SessionId, row.Request.TaskId, RequestOrigin.LocalUi, new(Guid.NewGuid())) },
        }];
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId, "absent", null, 25,
            f.Token, correlation: new(new(Guid.NewGuid()), new(Guid.NewGuid())))).Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task CorrelatedLargeBodiesRemainWholeAndByteBoundedWithoutSkippingOriginalRows()
    {
        using var f = new ConversationFixture();
        var original = Message(f, 1, "marker " + new string('\u0800', ConversationText.MaximumUtf16Characters - "marker ".Length));
        f.Messages = Enumerable.Range(1, 8).Select(index => original with { Sequence = index }).ToArray();
        var correlation = new ConversationCorrelationFilter(original.Request.RequestId, original.Request.TaskId);
        var range = new ConversationRangeFilter(1, 100, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        ConversationSearchCursor? cursor = null;
        var rows = new List<ConversationMessage>();
        do
        {
            var page = await f.Service.SearchConversationAsync(f.Session.SessionId, "marker", cursor, 50, f.Token,
                terminal: new(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel, HostTaskState.Unavailable),
                range: range, correlation: correlation);
            ConversationSearchPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
            page.Messages.Length.Should().BeInRange(1, 2);
            page.Scanned.Should().Be(page.Messages.Length);
            rows.AddRange(page.Messages);
            cursor = page.Next;
        } while (cursor is not null);
        rows.Should().Equal(f.Messages);
    }
}
