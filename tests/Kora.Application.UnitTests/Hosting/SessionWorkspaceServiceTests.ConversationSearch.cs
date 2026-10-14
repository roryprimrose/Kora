using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(ConversationSearchRole.All, "Unavailable", 2)]
    [InlineData(ConversationSearchRole.USER, "Unavailable", 1)]
    [InlineData(ConversationSearchRole.HOST, "Unavailable", 2)]
    [InlineData(ConversationSearchRole.HOST, "body", 0)]
    [InlineData(ConversationSearchRole.USER, "body UnregisteredModel", 1)]
    public async Task RoleSelectsFieldsNotStoredRowsOrPageAdmission(ConversationSearchRole role, string query, int count)
    {
        using var f = new ConversationFixture();
        f.Messages = [Message(f, 1, "body Unavailable"), Message(f, 2, "other")];
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var page = await f.Service.SearchConversationAsync(f.Session.SessionId, query, null, 25, f.Token, role);
        page.Messages.Should().HaveCount(count);
        page.Scanned.Should().Be(2);
        page.Role.Should().Be(role);
        page.Next.Should().BeNull();
        f.Messages[0].Body.Should().Be("body Unavailable");
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(ConversationSearchRole.USER)]
    [InlineData(ConversationSearchRole.HOST)]
    public async Task RoleContinuationBindsFilterAndDigestBeforeAnyRead(ConversationSearchRole role)
    {
        using var f = new ConversationFixture();
        f.Messages = [Message(f, 1, "Unavailable"), Message(f, 2, "Unavailable")];
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var first = await f.Service.SearchConversationAsync(f.Session.SessionId, "Unavailable", null, 1, f.Token, role);
        first.Next!.Role.Should().Be(role);
        var reads = f.Reads;
        foreach (var tampered in new[] { first.Next with { Role = ConversationSearchRole.All },
            first.Next with { Role = (ConversationSearchRole)99 }, first.Next with { QueryDigest = "wrong" } })
        {
            await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId,
                "Unavailable", tampered, 1, f.Token, role)).Should().ThrowAsync<ArgumentException>();
        }
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId,
            "Unavailable", first.Next, 1, f.Token, ConversationSearchRole.All)).Should().ThrowAsync<ArgumentException>();
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId,
            "Unavailable", null, 1, f.Token, (ConversationSearchRole)99)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        f.Reads.Should().Be(reads);
        var next = await f.Service.SearchConversationAsync(f.Session.SessionId, "UNAVAILABLE", first.Next, 1, f.Token, role);
        next.Messages.Single().Should().Be(f.Messages[1]);
        next.Next.Should().BeNull();
    }

    [Theory]
    [InlineData("body")]
    [InlineData("status")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("sequence")]
    [InlineData("revision")]
    public async Task HostNonmatchesStillValidateEveryConsumedSourceRow(string invalid)
    {
        using var f = new ConversationFixture();
        var message = Message(f, 2, "body");
        message = invalid switch
        {
            "body" => message with { Body = "\ud800" },
            "status" => message with { Reason = (ModelTurnReason)99 },
            "session" => message with { Request = HostRequest.Create(RequestOrigin.LocalUi) },
            "generation" => message with { Generation = new(2) },
            "sequence" => message with { Sequence = 3 },
            _ => message with { Revision = 1 },
        };
        f.Messages = [Message(f, 1, "body"), message];
        f.AfterRead = page => page with { Messages = f.Messages.ToImmutableArray() };
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId,
            "body", null, 25, f.Token, ConversationSearchRole.HOST)).Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData(25)]
    [InlineData(50)]
    public async Task ConversationSearchUsesOriginalBodiesAcrossSourcePagesWithoutWrites(int limit)
    {
        using var f = new ConversationFixture();
        f.Messages = Enumerable.Range(1, ConversationText.MaximumSessionMessages)
            .Select(index => Message(f, index, index % 2 == 0 ? "exact café\r\n e\u0301" : "unmatched")).ToArray();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var first = await f.Service.SearchConversationAsync(f.Session.SessionId, "CAFE\u0301 café", null, limit, f.Token);
        first.Messages.Should().HaveCount(limit);
        first.Scanned.Should().Be(limit * 2);
        var records = first.Messages.ToList();
        if (first.Next is { } cursor)
        {
            var next = await f.Service.SearchConversationAsync(f.Session.SessionId, "café", cursor, limit, f.Token);
            records.AddRange(next.Messages);
            next.Next.Should().BeNull();
        }
        records.Select(row => row.Sequence).Should().Equal(Enumerable.Range(1, 50).Select(index => (long)index * 2));
        records.Should().OnlyContain(row => row.Body == "exact café\r\n e\u0301");
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Fact]
    public async Task ConversationSearchDefersWholeByteBoundedRowsAndNeverSkipsOrDuplicates()
    {
        using var f = new ConversationFixture();
        f.Messages = Enumerable.Range(1, 8).Select(index => Message(f, index, "word " + new string('\u0800', 4091))).ToArray();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        ConversationSearchCursor? cursor = null;
        var records = new List<ConversationMessage>();
        do
        {
            var page = await f.Service.SearchConversationAsync(f.Session.SessionId, "word", cursor, 50, f.Token);
            ConversationSearchPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
            page.Messages.Length.Should().BeInRange(1, 2);
            page.Scanned.Should().Be(page.Messages.Length);
            records.AddRange(page.Messages);
            cursor = page.Next;
        } while (cursor is not null);
        records.Should().Equal(f.Messages);
    }

    [Fact]
    public async Task CompleteResultBudgetReservesCounterAndCursorGrowthBeforeNonmatchingTail()
    {
        using var f = new ConversationFixture();
        var digest = new ConversationSearch("word").Digest;
        f.Messages = Enumerable.Range(1, 100).Select(index => Message(f, index,
            index < 9 ? "word " + new string('\u0800', 500) + new string('a', 3591)
                : index == 9 ? "word " : "other")).ToArray();
        var naive = new ConversationSearchPage(f.Session, 200, 100, digest, f.Messages.Take(9).ToImmutableArray(), 9,
            new(new(f.Session.SessionId, new(1), 100, 9, 200), digest));
        var remaining = ConversationText.MaximumPageUtf8Bytes - ConversationSearchPage.Serialize(naive).Length;
        remaining.Should().BePositive();
        f.Messages[8] = f.Messages[8] with { Body = "word " + new string('\u0800', remaining / 6) + new string('a', remaining % 6) };
        f.Messages[8].Validate();
        ConversationSearchPage.Serialize(naive with { Messages = f.Messages.Take(9).ToImmutableArray() }).Length.Should().Be(65536);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var first = await f.Service.SearchConversationAsync(f.Session.SessionId, "word", null, 50, f.Token);
        first.Messages.Should().HaveCount(8);
        first.Scanned.Should().Be(8);
        var next = await f.Service.SearchConversationAsync(f.Session.SessionId, "word", first.Next, 50, f.Token);
        next.Messages.Single().Should().Be(f.Messages[8]);
        next.Scanned.Should().Be(92);
        next.Next.Should().BeNull();
        ConversationSearchPage.Serialize(first).Length.Should().BeLessThanOrEqualTo(65536);
        ConversationSearchPage.Serialize(next).Length.Should().BeLessThanOrEqualTo(65536);
    }

    [Fact]
    public async Task EmptyAndNoMatchAreExplicitExhaustedObservationsNotInventedRows()
    {
        using var f = new ConversationFixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var page = await f.Service.SearchConversationAsync(f.Session.SessionId, "absent", null, 25, f.Token);
        page.Messages.Should().BeEmpty();
        page.Next.Should().BeNull();
        page.Scanned.Should().Be(0);
        f.Messages = Enumerable.Range(1, ConversationText.MaximumSessionMessages).Select(index => Message(f, index, "actual")).ToArray();
        page = await f.Service.SearchConversationAsync(f.Session.SessionId, "absent", null, 25, f.Token);
        page.Scanned.Should().Be(ConversationText.MaximumSessionMessages);
        page.Messages.Should().BeEmpty();
        page.Next.Should().BeNull();
        f.Messages = [Message(f, 1, "actual")];
        page = await f.Service.SearchConversationAsync(f.Session.SessionId, "absent", null, 25, f.Token);
        page.Messages.Should().BeEmpty();
        page.Scanned.Should().Be(1);
        page.Next.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task InvalidSearchLimitsAreExplicitBeforeReadingContent(int limit)
    {
        using var f = new ConversationFixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId, "word", null, limit, f.Token))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        f.Reads.Should().Be(0);
    }

    [Fact]
    public async Task QueryMismatchAndInvalidQueryNeverReadAnyBody()
    {
        using var f = new ConversationFixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var cursor = new ConversationSearchCursor(new(f.Session.SessionId, new(1), 1, 0, 2), "wrong");
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId, "word", cursor, 25, f.Token))
            .Should().ThrowAsync<ArgumentException>();
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId, " ", null, 25, f.Token))
            .Should().ThrowAsync<ArgumentException>();
        f.Reads.Should().Be(0);
    }

    [Fact]
    public async Task PassiveSearchDiagnosticsNeverContainQueryBodyOrExceptionText()
    {
        using var f = new ConversationFixture();
        const string marker = "privateSearchMarker";
        f.Messages = [Message(f, 1, marker)];
        f.Logger.Enabled = true;
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        (await f.Service.SearchConversationAsync(f.Session.SessionId, marker, null, 25, f.Token)).Messages.Should().ContainSingle();
        f.AfterRead = _ => throw new IOException(marker);
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId, marker, null, 25, f.Token))
            .Should().ThrowAsync<IOException>();
        f.Logger.Messages.Should().NotBeEmpty().And.OnlyContain(message => !message.Contains(marker, StringComparison.Ordinal));
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("cancel")]
    public async Task RevocationWhileAuthoritativeReaderIsAwaitedNeverPublishesContent(string change)
    {
        using var f = new ConversationFixture();
        f.Messages = [Message(f, 1, "private original marker")];
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeConversationRead = async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var pending = f.Service.SearchConversationAsync(f.Session.SessionId, "marker", null, 25, cancellation.Token, ConversationSearchRole.HOST);
        await entered.Task;
        if (change is "private") { f.CanInspect = false; }
        if (change is "owner") { f.AdvanceRevision(); }
        if (change is "cancel") { cancellation.Cancel(); }
        release.TrySetResult();
        Func<Task> observe = async () => { await pending; };
        await observe.Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("session")]
    [InlineData("ceiling")]
    [InlineData("negative-ceiling")]
    [InlineData("gap")]
    [InlineData("generation")]
    [InlineData("head")]
    [InlineData("row-owner")]
    [InlineData("row-generation")]
    [InlineData("sequence")]
    [InlineData("beyond")]
    [InlineData("final")]
    [InlineData("empty-final")]
    [InlineData("head-final")]
    [InlineData("privacy")]
    [InlineData("epoch")]
    [InlineData("cancel")]
    public async Task SourceOrAdmissionChangesRefuseAllContent(string change)
    {
        using var f = new ConversationFixture();
        f.Messages = change is "empty-final" ? [] : [Message(f, 1, "word"), Message(f, 2, "word")];
        f.AfterRead = page =>
        {
            if (change is "session") { return page with { Session = page.Session with { SessionId = new(Guid.NewGuid()) } }; }
            if (change is "ceiling") { return page with { Ceiling = 101 }; }
            if (change is "negative-ceiling") { return page with { Ceiling = -1 }; }
            if (change is "gap") { return page with { Messages = [] }; }
            if (change is "generation" && f.Reads > 2) { return page with { Session = page.Session with { Generation = new(2) } }; }
            if (change is "head" && f.Reads > 2) { return page with { HeadRevision = page.HeadRevision + 1 }; }
            if (change is "row-owner") { return page with { Messages = [page.Messages[0] with { Request = HostRequest.Create(RequestOrigin.LocalUi) }] }; }
            if (change is "row-generation") { return page with { Messages = [page.Messages[0] with { Generation = new(2) }] }; }
            if (change is "sequence") { return page with { Messages = [page.Messages[0] with { Sequence = 2 }] }; }
            if (change is "beyond") { return page with { Ceiling = 0 }; }
            if (change is "final" && f.Reads == 2) { return page with { Session = page.Session with { IsActive = false } }; }
            if (change is "empty-final" && f.Reads == 2) { return page with { Ceiling = 1 }; }
            if (change is "head-final" && f.Reads == 2) { return page with { HeadRevision = page.HeadRevision + 1 }; }
            if (change is "privacy") { f.CanInspect = false; }
            if (change is "epoch") { f.AdvanceRevision(); }
            if (change is "cancel") { throw new OperationCanceledException(); }
            return page;
        };
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        if (change is "generation" or "head")
        {
            var first = await f.Service.SearchConversationAsync(f.Session.SessionId, "Unavailable", null, 1, f.Token, ConversationSearchRole.HOST);
            await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId, "Unavailable", first.Next, 1, f.Token, ConversationSearchRole.HOST))
                .Should().ThrowAsync<InvalidDataException>();
        }
        else
        {
            await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId, "Unavailable", null, 25, f.Token, ConversationSearchRole.HOST))
                .Should().ThrowAsync<Exception>();
        }
        f.TaskWrites.Should().BeEmpty();
    }

    private static ConversationMessage Message(Fixture f, long sequence, string body) =>
        new(new(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            new(1), sequence, 2, DateTimeOffset.UnixEpoch, body, ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel);

    private sealed class ConversationFixture : Fixture, ISessionConversationStore
    {
        internal ConversationMessage[] Messages { get; set; } = [];
        internal int Reads { get; private set; }
        internal Func<ConversationPage, ConversationPage>? AfterRead { get; set; }
        internal Func<CancellationToken, Task>? BeforeConversationRead { get; set; }
        internal Action<int>? BeforeExactRead { get; set; }
        internal int ExactReads { get; private set; }
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session,
            HostId<RequestIdentity> request, CancellationToken token)
        {
            ExactReads++;
            BeforeExactRead?.Invoke(ExactReads);
            return ValueTask.FromResult(Messages.Single(message => message.Request.RequestId == request));
        }
        public async ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session,
            ConversationCursor? cursor, int limit, CancellationToken token)
        {
            if (BeforeConversationRead is { } before) { await before(token); }
            token.ThrowIfCancellationRequested();
            Reads++;
            var ceiling = cursor?.Ceiling ?? Messages.Length;
            var after = cursor?.After ?? 0;
            var rows = Messages.Where(row => row.Sequence > after && row.Sequence <= ceiling).Take(limit).ToImmutableArray();
            var last = rows.LastOrDefault()?.Sequence ?? after;
            var head = cursor?.HeadRevision ?? Messages.Sum(row => row.Revision);
            var page = new ConversationPage(Session, head, ceiling, rows,
                last < ceiling ? new(session, Session.Generation, ceiling, last, head) : null);
            return AfterRead?.Invoke(page) ?? page;
        }
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request,
            HostRevision generation, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control,
            ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request,
            HostRevision generation, long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request,
            ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
    }
}
