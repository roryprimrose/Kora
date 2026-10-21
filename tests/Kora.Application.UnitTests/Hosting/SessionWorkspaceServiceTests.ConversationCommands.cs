using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Dependencies;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

[Collection("Host tracing")]
public sealed partial class SessionWorkspaceServiceTests
{
    private static ConversationSearchCommandService SearchCommands(ConversationFixture f, TimeProvider? time = null) =>
        new(f.Service, new(f, f, f.Service, f, new SearchTurns(), time ?? TimeProvider.System,
            NullLogger<SessionConversationService>.Instance), f, time ?? TimeProvider.System);

    private static SessionCommand SearchCommand(ConversationFixture f, string? query = "word") =>
        new(SessionCommandOperation.ConversationSearch, f.Session.SessionId.Value) { ConversationQuery = query };

    private static SessionCommand SearchNext(ConversationFixture f) =>
        new(SessionCommandOperation.ConversationSearchNext, f.Session.SessionId.Value);

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task OriginalSearchUsesNativeCanonicalPagesAndHostHeldCursorWithoutWrites(RequestOrigin origin, bool done)
    {
        using var f = new ConversationFixture();
        f.Session = f.Session with { IsActive = !done };
        const string query = "  WoRd e\u0301  ";
        f.Messages = Enumerable.Range(1, 30).Select(index => Message(f, index, "word original café")).ToArray();
        var service = SearchCommands(f);
        var first = (await service.ExecuteAsync(SearchCommand(f, query), origin, static () => true, f.Token))!;
        first.Messages.Should().Equal(f.Messages.Take(25));
        first.Next.Should().NotBeNull();
        first.Session.Should().Be(f.Session);
        ConversationSearchPage.Serialize(first).Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
        var next = (await service.ExecuteAsync(SearchNext(f) with { ConversationQuery = "caller replacement is ignored" },
            origin, static () => true, f.Token))!;
        next.Messages.Should().Equal(f.Messages.Skip(25));
        next.QueryDigest.Should().Be(new ConversationSearch(query).Digest);
        next.Next.Should().BeNull();
        await service.Invoking(value => value.ExecuteAsync(SearchNext(f), origin, static () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("expiry")]
    [InlineData("control")]
    [InlineData("private")]
    [InlineData("generation")]
    [InlineData("done")]
    [InlineData("foreign")]
    [InlineData("origin")]
    [InlineData("retire")]
    public async Task HostHeldContinuationConsumesAndRejectsChangedOriginalSource(string fault)
    {
        using var f = new ConversationFixture();
        var clock = new SearchTime();
        f.Messages = Enumerable.Range(1, 30).Select(index => Message(f, index, "word")).ToArray();
        var service = SearchCommands(f, clock);
        var source = true;
        await service.ExecuteAsync(SearchCommand(f), RequestOrigin.ActivatedVoice, () => source, f.Token);
        var next = SearchNext(f);
        if (fault is "source") { source = false; }
        if (fault is "expiry") { clock.Now = clock.Now.AddSeconds(60); }
        if (fault is "control") { f.AdvanceRevision(); }
        if (fault is "private") { f.CanInspect = false; }
        if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "done") { f.Session = f.Session with { IsActive = false }; }
        if (fault is "foreign") { next = next with { SessionId = Guid.NewGuid() }; }
        if (fault is "retire") { service.Retire(); }
        var reads = f.Reads;
        await service.Invoking(value => value.ExecuteAsync(next,
            fault is "origin" ? RequestOrigin.LocalUi : RequestOrigin.ActivatedVoice, static () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Reads.Should().Be(reads);
        source = true;
        clock.Now = DateTimeOffset.UnixEpoch;
        f.CanInspect = true;
        f.CanControl = true;
        await service.Invoking(value => value.ExecuteAsync(SearchNext(f), RequestOrigin.ActivatedVoice, static () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("***")]
    [InlineData("\ud800")]
    [InlineData(null)]
    public async Task InvalidQueryRefusesBeforeAnyBodyRead(string? query)
    {
        using var f = new ConversationFixture();
        var service = SearchCommands(f);
        await service.Invoking(value => value.ExecuteAsync(SearchCommand(f, query),
            RequestOrigin.LocalUi, static () => true, f.Token)).Should().ThrowAsync<ArgumentException>();
        f.Reads.Should().Be(0);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("source")]
    [InlineData("generation")]
    [InlineData("foreign")]
    [InlineData("retire")]
    [InlineData("cancel")]
    public async Task LateSourceLossNeverPublishesSearchContent(string fault)
    {
        using var f = new ConversationFixture();
        var source = true;
        f.Messages = [Message(f, 1, "word secret")];
        var service = SearchCommands(f);
        using var cancellation = new CancellationTokenSource();
        f.AfterRead = page =>
        {
            if (fault is "private") { f.CanInspect = false; }
            if (fault is "source") { source = false; }
            if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
            if (fault is "foreign") { f.Session = f.Session with { SessionId = new(Guid.NewGuid()) }; }
            if (fault is "retire") { service.Retire(); }
            if (fault is "cancel") { cancellation.Cancel(); }
            return page;
        };
        await service.Invoking(value => value.ExecuteAsync(SearchCommand(f), RequestOrigin.LocalUi,
            () => source, cancellation.Token)).Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationIsExactAndCancelsInFlightReadWithoutLeakingOrAllowingCallerCursor()
    {
        using var f = new ConversationFixture();
        var service = SearchCommands(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeConversationRead = async token =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var search = service.ExecuteAsync(SearchCommand(f), RequestOrigin.LocalUi, static () => true, f.Token);
        await entered.Task;
        var cancel = SearchNext(f) with { Operation = SessionCommandOperation.ConversationSearchCancel };
        await service.Invoking(value => value.ExecuteAsync(cancel with { SessionId = Guid.NewGuid() },
            RequestOrigin.LocalUi, static () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        (await service.ExecuteAsync(cancel, RequestOrigin.LocalUi, static () => true, f.Token)).Should().BeNull();
        await this.Invoking(_ => search).Should().ThrowAsync<OperationCanceledException>();
        service.Retire();
        await service.Invoking(value => value.ExecuteAsync(cancel, RequestOrigin.LocalUi, static () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ZeroMatchesAreTruthfulAndPendingContinuationCanBeCancelledWithoutReads()
    {
        using var f = new ConversationFixture();
        f.Messages = [Message(f, 1, "nonmatch")];
        var service = SearchCommands(f);
        var zero = (await service.ExecuteAsync(SearchCommand(f), RequestOrigin.LocalUi, static () => true, f.Token))!;
        zero.Messages.Should().BeEmpty();
        zero.Scanned.Should().Be(1);
        zero.Next.Should().BeNull();
        f.Messages = Enumerable.Range(1, 30).Select(index => Message(f, index, "word")).ToArray();
        await service.ExecuteAsync(SearchCommand(f), RequestOrigin.LocalUi, static () => true, f.Token);
        var reads = f.Reads;
        (await service.ExecuteAsync(SearchNext(f) with { Operation = SessionCommandOperation.ConversationSearchCancel },
            RequestOrigin.LocalUi, static () => true, f.Token)).Should().BeNull();
        f.Reads.Should().Be(reads);
    }

    [Fact]
    public async Task ThreePagesRetainOriginalExpiryAndAZeroMatchContinuationIsNotBrowse()
    {
        using var f = new ConversationFixture();
        var clock = new SearchTime();
        var service = SearchCommands(f, clock);
        f.Messages = Enumerable.Range(1, 80).Select(index => Message(f, index, index <= 50 ? "word" : "nonmatch")).ToArray();
        var first = (await service.ExecuteAsync(SearchCommand(f), RequestOrigin.LocalUi, static () => true, f.Token))!;
        first.Next.Should().NotBeNull();
        clock.Now = clock.Now.AddSeconds(59);
        var second = (await service.ExecuteAsync(SearchNext(f), RequestOrigin.LocalUi, static () => true, f.Token))!;
        second.Next.Should().NotBeNull();
        var zero = (await service.ExecuteAsync(SearchNext(f), RequestOrigin.LocalUi, static () => true, f.Token))!;
        zero.Messages.Should().BeEmpty();
        zero.Scanned.Should().Be(30);
        zero.Next.Should().BeNull();
        clock.Now = DateTimeOffset.UnixEpoch;
        await service.ExecuteAsync(SearchCommand(f), RequestOrigin.LocalUi, static () => true, f.Token);
        clock.Now = clock.Now.AddSeconds(59);
        await service.ExecuteAsync(SearchNext(f), RequestOrigin.LocalUi, static () => true, f.Token);
        clock.Now = clock.Now.AddSeconds(1);
        await service.Invoking(value => value.ExecuteAsync(SearchNext(f), RequestOrigin.LocalUi, static () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("missing-id")]
    [InlineData("empty-id")]
    [InlineData("cancelled")]
    [InlineData("source")]
    [InlineData("before-read")]
    [InlineData("after-target")]
    [InlineData("foreign-target")]
    [InlineData("metadata")]
    [InlineData("page-source")]
    [InlineData("publication")]
    public async Task EveryAdmissionBoundaryRefusesInvalidOrRacedInput(string fault)
    {
        using var f = new ConversationFixture();
        var service = SearchCommands(f);
        f.Messages = [Message(f, 1, "word")];
        var calls = 0;
        var metadataReads = 0;
        var command = SearchCommand(f);
        if (fault is "operation") { command = command with { Operation = SessionCommandOperation.Status }; }
        if (fault is "missing-id") { command = command with { SessionId = null }; }
        if (fault is "empty-id") { command = command with { SessionId = Guid.Empty }; }
        var expected = new SessionWorkspaceEntry(f.Session, null);
        if (fault is "foreign-target") { expected = expected with { Authority = f.Session with { SessionId = new(Guid.NewGuid()) } }; }
        if (fault is "page-source") { expected = expected with { Authority = f.Session with { IsActive = false } }; }
        f.ExactListReader = _ =>
        {
            metadataReads++;
            return fault is "metadata" && metadataReads == 2
                ? expected with { Metadata = new(f.Session.SessionId, new(1), new("changed")) } : expected;
        };
        bool Admitted()
        {
            calls++;
            return fault switch
            {
                "source" => false,
                "before-read" => calls < 2,
                "after-target" => calls < 3,
                "publication" => calls < 5,
                _ => true,
            };
        }
        using var cancellation = new CancellationTokenSource();
        if (fault is "cancelled") { cancellation.Cancel(); }
        await service.Invoking(value => value.ExecuteAsync(command, RequestOrigin.LocalUi, Admitted, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task ExactContinuationRejectsChangedMetadataAndCallerHistoryCursorsNeverSupplySearchAuthority()
    {
        using var f = new ConversationFixture();
        var service = SearchCommands(f);
        f.Messages = Enumerable.Range(1, 30).Select(index => Message(f, index, "word")).ToArray();
        await service.ExecuteAsync(SearchCommand(f), RequestOrigin.LocalUi, static () => true, f.Token);
        f.ExactListReader = _ => new(f.Session, new(f.Session.SessionId, new(1), new("changed")));
        await service.Invoking(value => value.ExecuteAsync(SearchNext(f), RequestOrigin.LocalUi, static () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.ExactListReader = null;
        var result = await service.ExecuteAsync(SearchCommand(f) with
        {
            HistorySearchCursor = new(new(new(Guid.NewGuid()), new(99), 99, 99), "caller cursor"),
        }, RequestOrigin.LocalUi, static () => true, f.Token);
        result!.Messages.First().Sequence.Should().Be(1);
        var fresh = (await service.ExecuteAsync(SearchCommand(f), RequestOrigin.LocalUi, static () => true, f.Token))!;
        fresh.Messages.Should().Equal(result.Messages);
    }

    [Fact]
    public async Task MissingLiveTraceFailsClosedWithoutFabricatingParentOrReadingBodies()
    {
        using var f = new ConversationFixture();
        f.Dispose();
        f.Messages = Enumerable.Range(1, 30).Select(index => Message(f, index, "word")).ToArray();
        var service = SearchCommands(f);
        using var incoming = new Activity("untrusted incoming").Start();
        await service.Invoking(value => value.ExecuteAsync(SearchCommand(f), RequestOrigin.ActivatedVoice, static () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Reads.Should().Be(0);
        f.TaskWrites.Should().BeEmpty();
        Activity.Current.Should().BeSameAs(incoming);
    }

    [Fact]
    public async Task ContinuationLinksSeparateHostRootsWithContentFreeOriginalSessionAndTerminalOutcomes()
    {
        using var f = new ConversationFixture();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        f.Messages = Enumerable.Range(1, 30).Select(index => Message(f, index, "private word")).ToArray();
        var service = SearchCommands(f);
        using var incoming = new Activity("untrusted incoming").Start();
        await service.ExecuteAsync(SearchCommand(f), RequestOrigin.ActivatedVoice, static () => true, f.Token);
        await service.ExecuteAsync(SearchNext(f), RequestOrigin.ActivatedVoice, static () => true, f.Token);
        var roots = stopped.Where(activity => activity.ParentId is null).ToArray();
        roots.Should().HaveCount(2);
        roots[1].Links.Single().Context.Should().Be(roots[0].Context);
        roots.Should().OnlyContain(activity => activity.IdFormat == ActivityIdFormat.W3C
            && activity.TraceId != incoming.TraceId && activity.Duration >= TimeSpan.Zero);
        roots.Select(activity => activity.GetTagItem("kora.session.id")).Should().OnlyContain(value => Equals(value, f.Session.SessionId.Value));
        stopped.SelectMany(activity => activity.TagObjects).Select(tag => tag.Value?.ToString())
            .Should().NotContain(value => value != null && value.Contains("word", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData((RequestOrigin)99)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task ModelAndHostOriginsNeverAcquireOriginalUserSearchAuthority(RequestOrigin origin)
    {
        using var f = new ConversationFixture();
        await SearchCommands(f).Invoking(value => value.ExecuteAsync(SearchCommand(f), origin, static () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Reads.Should().Be(0);
    }

    private sealed class SearchTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class SearchTurns : IModelTurnAdmission
    {
        public bool IsQuiescent => throw new NotSupportedException();
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token) => throw new NotSupportedException();
    }
}
