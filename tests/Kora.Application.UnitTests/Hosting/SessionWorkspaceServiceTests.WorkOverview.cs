using System.Collections.Immutable;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task SmallerQueryContractStillEnforcesTheExactCompletePageByteCeilingWithoutSkippingRows()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, SessionWorkOverviewPage.MaximumSessions).Select(id => ListEntry(id)).ToImmutableArray();
        OverviewReader(f, entries);
        f.OverviewReader = (id, _) =>
        {
            var snapshot = OverviewSnapshot(entries.Single(entry => entry.Authority.SessionId == id));
            return Task.FromResult(snapshot with { Tasks = [.. Enumerable.Repeat(snapshot.Tasks[0], 50)] });
        };
        var page = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
        page.OutputLimited.Should().BeTrue();
        page.Records.Length.Should().BeInRange(1, SessionWorkOverviewPage.MaximumSessions - 1);
        page.Scanned.Should().Be(page.Records.Length);
        SessionWorkOverviewPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(SessionWorkOverviewPage.MaximumResultBytes);
        var observed = page.Records.ToList();
        while (page.Next is { } next)
        {
            page = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, next, f.Token);
            observed.AddRange(page.Records);
        }
        observed.Select(row => row.Session).Should().Equal(entries);
    }
    [Theory]
    [InlineData(SessionListSearchKind.NameSubstring, SessionListFilter.All, false, 1)]
    [InlineData(SessionListSearchKind.NameSubstring, SessionListFilter.Active, true, 1)]
    [InlineData(SessionListSearchKind.NameSubstring, SessionListFilter.Done, false, 0)]
    [InlineData(SessionListSearchKind.ExactId, SessionListFilter.All, true, 1)]
    [InlineData(SessionListSearchKind.ExactId, SessionListFilter.Active, false, 1)]
    [InlineData(SessionListSearchKind.ExactId, SessionListFilter.Done, true, 0)]
    public async Task OverviewMetadataQueryComposesFiltersAndScansPastEmptyPagesWithoutObservingNonmatches(
        SessionListSearchKind kind, SessionListFilter filter, bool needsAnswer, int expected)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, 12).Select(id => ListEntry(id,
            id == 12 ? "Private café_100% target" : id == 11 ? null : "PRIVATE CAFÉ_100% target", id % 2 == 0)).ToImmutableArray();
        OverviewReader(f, entries);
        var observed = new List<HostId<SessionIdentity>>();
        f.OverviewReader = (id, _) =>
        {
            observed.Add(id);
            return Task.FromResult(OverviewSnapshot(entries.Single(entry => entry.Authority.SessionId == id), 1));
        };
        f.Logger.Enabled = true;
        var query = new SessionListSearch(kind, filter,
            kind == SessionListSearchKind.ExactId ? entries[^1].Authority.SessionId.Value.ToString("D") : "café_100%");
        var first = await f.Service.ReadWorkOverviewAsync(filter, needsAnswer, null, f.Token, query);
        first.Records.Should().BeEmpty();
        first.Scanned.Should().Be(10);
        first.Nonmatches.Should().Be(10);
        first.Next!.QueryDigest.Should().Be(query.Digest);
        observed.Should().BeEmpty();
        var second = await f.Service.ReadWorkOverviewAsync(filter, needsAnswer, first.Next, f.Token, query);
        second.Scanned.Should().Be(2);
        second.Nonmatches.Should().Be(2 - expected);
        second.Records.Should().HaveCount(expected);
        second.Next.Should().BeNull();
        observed.Should().HaveCount(expected);
        if (expected == 1) { observed.Single().Should().Be(entries[^1].Authority.SessionId); }
        f.Logger.Messages.Should().OnlyContain(message => !message.Contains(query.Value, StringComparison.Ordinal));
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("case")]
    [InlineData("kind")]
    [InlineData("clear")]
    [InlineData("added")]
    [InlineData("digest")]
    [InlineData("query-filter")]
    public async Task OverviewRejectsQueryContinuationMismatchBeforeReadingMetadata(string change)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        OverviewReader(f, [.. Enumerable.Range(1, 11).Select(id => ListEntry(id))]);
        SessionListSearch? query = new(SessionListSearchKind.NameSubstring, SessionListFilter.All, "needle");
        var first = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token,
            change is "added" ? null : query);
        var cursor = first.Next!;
        query = change switch
        {
            "query" => new(SessionListSearchKind.NameSubstring, SessionListFilter.All, "other"),
            "case" => new(SessionListSearchKind.NameSubstring, SessionListFilter.All, "Needle"),
            "kind" => new(SessionListSearchKind.ExactId, SessionListFilter.All, ListEntry(11).Authority.SessionId.Value.ToString("D")),
            "clear" => null,
            "query-filter" => new(SessionListSearchKind.NameSubstring, SessionListFilter.Done, "needle"),
            _ => query
        };
        if (change is "digest") { cursor = cursor with { QueryDigest = string.Empty }; }
        var read = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, cursor, f.Token, query);
        await read.Should().ThrowAsync<ArgumentException>();
        f.ListReads.Should().Be(1);
    }

    [Fact]
    public async Task QueryUsesMutableCanonicalKeysetAndFreshScanSeesBehindCursorChanges()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, 11).Select(id => ListEntry(id, "other")).ToImmutableArray();
        OverviewReader(f, entries);
        var query = new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.All, "needle");
        var first = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token, query);
        entries = entries.SetItem(0, ListEntry(1)).SetItem(10, ListEntry(11));
        OverviewReader(f, entries);
        var next = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, first.Next, f.Token, query);
        next.Records.Single().Session.Should().Be(entries[^1]);
        var fresh = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token, query);
        fresh.Records.Single().Session.Should().Be(entries[0]);
    }

    [Fact]
    public async Task ExactIdQueryCanMatchUnnamedMetadataAndKeepsUnavailableQuestionSourceExplicit()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1, null);
        OverviewReader(f, [entry]);
        f.OverviewReader = (_, _) => throw new IOException("private source unavailable");
        var query = new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, entry.Authority.SessionId.Value.ToString("D"));
        var page = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, f.Token, query);
        page.Records.Single().Availability.Should().Be(SessionWorkOverviewAvailability.ReadUnavailable);
        page.Nonmatches.Should().Be(0);
        page.Scanned.Should().Be(1);
    }

    [Fact]
    public async Task IndependentSourceActivitiesCarryOnlyTheirExactHostResolvedSessionAndContentFreeCounts()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = new[] { ListEntry(1, "Private title never logged"), ListEntry(2, "Other private title") }.ToImmutableArray();
        OverviewReader(f, entries);
        f.Logger.Enabled = true;
        var observed = new List<HostId<SessionIdentity>>();
        var traces = new List<System.Diagnostics.ActivityTraceId>();
        f.OverviewReader = (id, _) =>
        {
            var current = HostActivity.RequireCurrent();
            current.Request.SessionId.Should().Be(id);
            current.Activity!.ParentSpanId.Should().NotBe(default(System.Diagnostics.ActivitySpanId));
            current.Activity.TraceId.Should().NotBe(root.Activity!.TraceId);
            observed.Add(id);
            traces.Add(current.Activity.TraceId);
            return Task.FromResult(OverviewSnapshot(entries.Single(entry => entry.Authority.SessionId == id)));
        };
        await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
        observed.Should().Equal(entries.Select(entry => entry.Authority.SessionId));
        traces.Distinct().Should().HaveCount(2);
        f.Logger.Events.Should().Contain(e => e.Name == "OverviewRead");
        f.Logger.Messages.Should().OnlyContain(message => !message.Contains("Private title", StringComparison.Ordinal)
            && !message.Contains("Other private", StringComparison.Ordinal));
        HostActivity.RequireCurrent().Should().Be(root);
    }

    [Fact]
    public async Task UnexpectedSourceFailureRejectsThePageRatherThanFabricatingAnUnavailableSuccess()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        OverviewReader(f, [ListEntry(1)]);
        f.OverviewReader = (_, _) => throw new NotSupportedException("unsupported source");
        var read = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
        await read.Should().ThrowAsync<NotSupportedException>();
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("cancel")]
    [InlineData("cancel-timeout")]
    [InlineData("source")]
    public async Task OverviewFencesDeterministicInFlightCancellationTimeoutAndSourceRevocation(string mode)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1);
        OverviewReader(f, [entry]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SessionWorkSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OverviewReader = (_, _) => { entered.SetResult(); return release.Task; };
        var clock = new OverviewTime();
        var service = new SessionWorkspaceService(f, new(f), f, f.Logger, overviewTime: clock);
        using var cancellation = new CancellationTokenSource();
        var query = new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.All, "needle");
        var pending = service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, cancellation.Token, query);
        await entered.Task.WaitAsync(f.Token);
        if (mode is "source")
        {
            var invalidated = false;
            service.WorkOverviewInvalidated += _ => invalidated = true;
            await service.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
            invalidated.Should().BeTrue();
            release.SetResult(OverviewSnapshot(entry));
        }
        else
        {
            if (mode is "cancel" or "cancel-timeout") { cancellation.Cancel(); }
            if (mode is "timeout" or "cancel-timeout") { clock.Fire(); }
        }
        var act = async () => await pending;
        if (mode is "timeout") { await act.Should().ThrowAsync<TimeoutException>(); }
        else if (mode is "source") { await act.Should().ThrowAsync<InvalidOperationException>(); }
        else { await act.Should().ThrowAsync<OperationCanceledException>(); }
        release.TrySetResult(OverviewSnapshot(entry));
        clock.Timer!.Disposed.Should().BeTrue();
    }

    private sealed class OverviewTime : TimeProvider
    {
        internal OverviewTimer? Timer { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            dueTime.Should().Be(SessionWorkspaceService.WorkOverviewTimeout);
            Timer = new(callback, state);
            return Timer;
        }
        internal void Fire() => Timer!.Fire();
        internal sealed class OverviewTimer(TimerCallback callback, object? state) : ITimer
        {
            internal bool Disposed { get; private set; }
            internal void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private static SessionWorkSnapshot OverviewSnapshot(SessionWorkspaceEntry entry, int questions = 0)
    {
        var request = new HostRequest(new(Guid.NewGuid()), entry.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        return new(entry, 41, DateTimeOffset.Parse("2026-10-10T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            new(entry.Authority.SessionId, entry.Authority.Generation, 37, []), 0, 8, 2,
            [], 4, [new(new(request, new(3), HostTaskState.Unknown), entry.Authority.Generation, "PRIVATE diagnostic body excluded", false, null)], 9,
            [.. Enumerable.Range(0, questions).Select(_ => new SessionWorkQuestion(
                new(request, new(Guid.NewGuid()), new(5)), entry.Authority.Generation, QuestionStatus.Pending,
                QuestionKind.SingleChoice, RequestOrigin.LocalUi, DateTimeOffset.MaxValue))], 0);
    }

    private static void OverviewReader(Fixture fixture, ImmutableArray<SessionWorkspaceEntry> entries)
    {
        ListReader(fixture, entries);
        fixture.OverviewReader = (id, _) => Task.FromResult(OverviewSnapshot(entries.Single(entry => entry.Authority.SessionId == id)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(31)]
    public async Task OverviewUsesFiniteIndependentPagesWithoutWritesOrInventedGlobalTotals(int count)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, count).Select(id => ListEntry(id, active: id % 2 == 0)).ToImmutableArray();
        OverviewReader(f, entries);
        var rows = new List<SessionWorkOverviewRow>();
        SessionWorkOverviewCursor? cursor = null;
        do
        {
            var page = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, cursor, f.Token);
            page.Scanned.Should().BeLessThanOrEqualTo(10);
            page.Nonmatches.Should().Be(0);
            SessionWorkOverviewPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(65536);
            rows.AddRange(page.Records);
            cursor = page.Next;
        } while (cursor is not null);
        rows.Select(row => row.Session).Should().Equal(entries);
        rows.Should().OnlyContain(row => row.Availability == SessionWorkOverviewAvailability.Observed);
        rows.Should().OnlyContain(row => row.Work!.AuthorityRevision == 41 && row.Work.QueueRevision == 37
            && row.Work.ExecutionSlots == 2 && row.Work.PendingCapacity == 8 && row.Work.OmittedTasks == 9);
        Encoding.UTF8.GetString(SessionWorkOverviewPage.Serialize(new([.. rows.Take(10)], 10, 0, false, null)))
            .Should().NotContain("PRIVATE diagnostic");
        f.ListReads.Should().Be(Math.Max(1, (count + 9) / 10));
        f.ControlCalls.Should().Be(0);
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SessionListFilter.All, false, 11)]
    [InlineData(SessionListFilter.Active, false, 5)]
    [InlineData(SessionListFilter.Done, false, 6)]
    [InlineData(SessionListFilter.All, true, 1)]
    public async Task OverviewFiltersRemainSnapshotScopedAndContinueAfterZeroMatches(SessionListFilter filter, bool needsAnswer, int expected)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, 11).Select(id => ListEntry(id, active: id % 2 == 0)).ToImmutableArray();
        OverviewReader(f, entries);
        f.OverviewReader = (id, _) => Task.FromResult(OverviewSnapshot(entries.Single(entry => entry.Authority.SessionId == id),
            id == entries[^1].Authority.SessionId ? 1 : 0));
        var first = await f.Service.ReadWorkOverviewAsync(filter, needsAnswer, null, f.Token);
        first.Next.Should().NotBeNull();
        if (needsAnswer) { first.Records.Should().BeEmpty(); first.Scanned.Should().Be(10); }
        var second = await f.Service.ReadWorkOverviewAsync(filter, needsAnswer, first.Next, f.Token);
        first.Records.Length.Should().Be(expected - second.Records.Length);
        second.Next.Should().BeNull();
        first.Nonmatches.Should().Be(first.Scanned - first.Records.Length);
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("needs")]
    [InlineData("host")]
    [InlineData("revision")]
    [InlineData("empty")]
    [InlineData("epoch")]
    [InlineData("query-epoch")]
    [InlineData("query-revision")]
    public async Task OverviewContinuationRejectsChangedQueryAdmissionOrSource(string mode)
    {
        using var f = new Fixture();
        using var other = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        OverviewReader(f, [.. Enumerable.Range(1, 11).Select(id => ListEntry(id))]);
        var first = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
        var cursor = first.Next!;
        if (mode is "revision") { f.AdvanceRevision(); }
        if (mode is "empty") { cursor = cursor with { After = Guid.Empty }; }
        if (mode is "epoch") { cursor = cursor with { SourceEpoch = cursor.SourceEpoch + 1 }; }
        SessionListSearch? query = null;
        if (mode is "query-epoch" or "query-revision")
        {
            query = new(SessionListSearchKind.NameSubstring, SessionListFilter.All, "needle");
            cursor = (await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token, query)).Next!;
            if (mode is "query-epoch")
            {
                await f.Service.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
            }
            else { f.AdvanceRevision(); }
        }
        var act = () => (mode is "host" ? other.Service : f.Service).ReadWorkOverviewAsync(
            mode is "filter" ? SessionListFilter.Done : SessionListFilter.All, mode is "needs", cursor, f.Token, query);
        await act.Should().ThrowAsync<ArgumentException>();
        f.ListReads.Should().Be(mode is "query-epoch" or "query-revision" ? 2 : 1);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("oversize")]
    [InlineData("empty-next")]
    [InlineData("zero-next")]
    [InlineData("wrong-next")]
    [InlineData("order")]
    [InlineData("filter")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("cancel")]
    [InlineData("late-cancel")]
    [InlineData("read")]
    public async Task OverviewRejectsMalformedMetadataOrExpiredAdmissionWithoutPartialSuccess(string mode)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1);
        OverviewReader(f, [entry]);
        var page = new SessionPage<SessionWorkspaceEntry>([entry], null);
        if (mode is "default") { page = new(default, null); }
        if (mode is "oversize") { page = new([.. Enumerable.Range(1, 11).Select(id => ListEntry(id))], null); }
        if (mode is "empty-next") { page = new([], entry.Authority.SessionId.Value); }
        if (mode is "zero-next") { page = page with { Next = Guid.Empty }; }
        if (mode is "wrong-next") { page = page with { Next = Guid.NewGuid() }; }
        if (mode is "order") { page = new([entry, entry], null); }
        f.ListReader = (_, _) => mode is "read" ? throw new IOException() : page;
        if (mode is "privacy") { f.AfterListRead = () => f.CanInspect = false; }
        if (mode is "revision") { f.AfterListRead = f.AdvanceRevision; }
        using var cancellation = new CancellationTokenSource();
        if (mode is "cancel") { cancellation.Cancel(); }
        if (mode is "late-cancel") { f.AfterListRead = cancellation.Cancel; }
        var act = () => f.Service.ReadWorkOverviewAsync(mode is "filter" ? (SessionListFilter)99 : SessionListFilter.All,
            false, null, cancellation.Token);
        await act.Should().ThrowAsync<Exception>();
        f.ControlCalls.Should().Be(0);
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("generation", SessionWorkOverviewAvailability.SourceChanged)]
    [InlineData("lifecycle", SessionWorkOverviewAvailability.SourceChanged)]
    [InlineData("metadata", SessionWorkOverviewAvailability.SourceChanged)]
    [InlineData("missing", SessionWorkOverviewAvailability.ReadUnavailable)]
    [InlineData("corrupt", SessionWorkOverviewAvailability.ReadUnavailable)]
    [InlineData("owner", SessionWorkOverviewAvailability.ReadUnavailable)]
    [InlineData("unconfirmed", SessionWorkOverviewAvailability.ReadUnavailable)]
    public async Task OverviewKeepsUnavailableSourcesExplicitEvenUnderNeedsAnswer(string mode, SessionWorkOverviewAvailability expected)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1);
        OverviewReader(f, [entry]);
        f.OverviewReader = (_, _) =>
        {
            if (mode is "missing") { throw new IOException(); }
            if (mode is "corrupt") { throw new InvalidDataException(); }
            if (mode is "owner") { throw new UnauthorizedAccessException(); }
            if (mode is "unconfirmed") { throw new InvalidOperationException(); }
            var changed = entry with
            {
                Authority = entry.Authority with
                {
                    Generation = new(mode is "generation" ? 2 : 1),
                    IsActive = mode is not "lifecycle"
                },
                Metadata = entry.Metadata! with { Revision = new(mode is "metadata" ? 2 : 1) }
            };
            return Task.FromResult(OverviewSnapshot(changed));
        };
        var result = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, f.Token);
        result.Nonmatches.Should().Be(0);
        var row = result.Records.Should().ContainSingle().Which;
        row.Availability.Should().Be(expected);
        row.Work.Should().BeNull("previous private work is not presented as current");
    }

    [Theory]
    [InlineData("tasks-default")]
    [InlineData("tasks-over")]
    [InlineData("questions-default")]
    [InlineData("questions-over")]
    [InlineData("queue-default")]
    [InlineData("task-omission")]
    [InlineData("question-omission")]
    [InlineData("queue-omission")]
    [InlineData("task-owner")]
    [InlineData("question-owner")]
    [InlineData("queue-owner")]
    public async Task OverviewRejectsUnboundedOrForeignWorkRecords(string mode)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1);
        OverviewReader(f, [entry]);
        var snapshot = OverviewSnapshot(entry, 1);
        var foreign = OverviewSnapshot(ListEntry(2), 1);
        snapshot = mode switch
        {
            "tasks-default" => snapshot with { Tasks = default },
            "tasks-over" => snapshot with { Tasks = [.. Enumerable.Repeat(snapshot.Tasks[0], 51)] },
            "questions-default" => snapshot with { PendingQuestions = default },
            "questions-over" => snapshot with { PendingQuestions = [.. Enumerable.Repeat(snapshot.PendingQuestions[0], 51)] },
            "queue-default" => snapshot with { QueueRecords = default },
            "task-omission" => snapshot with { OmittedTasks = -1 },
            "question-omission" => snapshot with { OmittedQuestions = -1 },
            "queue-omission" => snapshot with { OmittedQueueRecords = -1 },
            "task-owner" => snapshot with { Tasks = foreign.Tasks },
            "question-owner" => snapshot with { PendingQuestions = foreign.PendingQuestions },
            _ => snapshot with { QueueRecords = [new(new(foreign.Tasks[0].Task.Request, new(1), new(1), 1,
                SessionQueueState.Pending, Guid.NewGuid(), 1, snapshot.ObservedAt, snapshot.ObservedAt.AddMinutes(30)),
                SessionQueueEligibility.Ready, null)] }
        };
        f.OverviewReader = (_, _) => Task.FromResult(snapshot);
        var page = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
        page.Records.Single().Availability.Should().Be(SessionWorkOverviewAvailability.ReadUnavailable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteBudgetNeverSkipsAnUnconsumedSessionAndMarksAnOversizedSingleGroupUnavailable(bool queried)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, 3).Select(id => ListEntry(id)).ToImmutableArray();
        OverviewReader(f, entries);
        f.OverviewReader = (id, _) =>
        {
            var snapshot = OverviewSnapshot(entries.Single(entry => entry.Authority.SessionId == id));
            var queueEntry = new SessionQueueEntry(snapshot.Tasks[0].Task.Request, new(1), new(1), 1,
                SessionQueueState.Pending, Guid.NewGuid(), 1, snapshot.ObservedAt, snapshot.ObservedAt.AddMinutes(30));
            return Task.FromResult(snapshot with { Tasks = [.. Enumerable.Repeat(snapshot.Tasks[0], 50)],
                QueueRecords = id == entries[2].Authority.SessionId
                    ? [.. Enumerable.Repeat(new SessionQueueObservation(queueEntry, SessionQueueEligibility.Ready, null), 50)] : [],
                PendingQuestions = OverviewSnapshot(snapshot.Session, id == entries[2].Authority.SessionId ? 50 : 0).PendingQuestions });
        };
        var seen = new List<SessionWorkOverviewRow>();
        var query = queried ? new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.All, "needle") : null;
        SessionWorkOverviewCursor? cursor = null;
        do
        {
            var page = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, cursor, f.Token, query);
            SessionWorkOverviewPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(65536);
            seen.AddRange(page.Records);
            cursor = page.Next;
        } while (cursor is not null);
        seen.Select(row => row.Session).Should().Equal(entries);
        seen[^1].Availability.Should().Be(SessionWorkOverviewAvailability.OutputBound);
    }

    [Fact]
    public async Task ObservedQuestionOmissionsCannotBeInterpretedAsNoNeedsAnswer()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1);
        OverviewReader(f, [entry]);
        f.OverviewReader = (_, _) => Task.FromResult(OverviewSnapshot(entry) with { OmittedQuestions = 7 });
        var page = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, f.Token);
        page.Records.Single().Work!.OmittedQuestions.Should().Be(7);
    }

    [Fact]
    public async Task CapturedQueueDeadlinesAndAuthoritativeReasonsPassThroughWithoutViewPolicy()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1);
        OverviewReader(f, [entry]);
        var snapshot = OverviewSnapshot(entry);
        var request = snapshot.Tasks[0].Task.Request;
        var queue = new SessionQueueEntry(request, new(1), new(1), 1, SessionQueueState.Pending, Guid.NewGuid(),
            1, snapshot.ObservedAt, snapshot.ObservedAt.AddMinutes(17), RecordVersion: 2, PendingLifetimeMinutes: 17)
            .Admit(new(2), 2, snapshot.ObservedAt.AddMinutes(1), 11);
        var observation = new SessionQueueObservation(queue, SessionQueueEligibility.Current, queue.ActiveDeadlineAt);
        f.OverviewReader = (_, _) => Task.FromResult(snapshot with { QueueRecords = [observation] });
        var page = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
        page.Records[0].Work!.QueueRecords.Should().Equal(observation);
        page.Records[0].Work!.ObservedCurrentFixedReads.Should().Be(1);
    }
}
