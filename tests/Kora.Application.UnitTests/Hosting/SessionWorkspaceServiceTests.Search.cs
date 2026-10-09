using System.Collections.Immutable;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    private static SessionHistoryEvent SearchRecord(Fixture f, long sequence, string? text = "needle") =>
        new(Guid.NewGuid(), f.Request.SessionId, sequence, new(1), SessionHistoryKind.Question,
            SessionHistoryAvailability.Available, null, null, null, 1, new('a', 64), null,
            false, text, [], null, [], null, null, null, null);

    private static void SearchReader(Fixture f, ImmutableArray<SessionHistoryEvent> records)
    {
        f.HistoryReader = (session, cursor, limit) =>
        {
            var snapshot = cursor?.Snapshot ?? records.Length;
            var page = records.Where(record => record.Sequence > (cursor?.After ?? 0) && record.Sequence <= snapshot)
                .Take(limit).ToImmutableArray();
            return new(session, cursor?.Generation ?? new(1), false, snapshot, page,
                page.Length != 0 && page[^1].Sequence < snapshot ? new(session, new(1), snapshot, page[^1].Sequence) : null);
        };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(201)]
    public async Task Search_limits_sequence_snapshot_and_empty_scan_continuation_are_explicit(int count)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        SearchReader(f, [.. Enumerable.Range(1, count).Select(i => SearchRecord(f, i, i == count ? "needle" : "other"))]);
        var first = await f.Service.SearchHistoryAsync(f.Request.SessionId, "needle", null, 50, f.Token);
        first.Scanned.Should().Be(Math.Min(count, 200));
        first.Records.Length.Should().Be(count is > 0 and <= 200 ? 1 : 0);
        if (count > 200)
        {
            first.Next.Should().NotBeNull();
            var last = await f.Service.SearchHistoryAsync(f.Request.SessionId, "NEEDLE needle", first.Next, 50, f.Token);
            last.Records.Should().ContainSingle().Which.Sequence.Should().Be(201);
            last.Next.Should().BeNull();
        }
        else { first.Next.Should().BeNull(); }
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public async Task Matching_results_stop_at_exact_requested_limit(int limit)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        SearchReader(f, [.. Enumerable.Range(1, 51).Select(i => SearchRecord(f, i))]);
        var result = await f.Service.SearchHistoryAsync(f.Request.SessionId, "needle", null, limit, f.Token);
        result.Records.Should().HaveCount(limit);
        result.Scanned.Should().Be(limit);
        result.Next!.History.After.Should().Be(limit);
    }

    [Fact]
    public async Task Search_reports_all_gaps_and_byte_omissions_without_truncating_or_losing_continuation()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        SearchReader(f, [SearchRecord(f, 1) with { Baseline = true },
            SearchRecord(f, 2) with { Availability = SessionHistoryAvailability.Gap },
            SearchRecord(f, 3) with { Availability = SessionHistoryAvailability.Redacted },
            SearchRecord(f, 4) with { Availability = SessionHistoryAvailability.Unavailable },
            SearchRecord(f, 5, "needle " + new string('界', 14000)),
            SearchRecord(f, 6, "needle " + new string('x', 40000))]);
        var first = await f.Service.SearchHistoryAsync(f.Request.SessionId, "needle", null, 50, f.Token);
        first.Gaps.Should().Be(4);
        first.Records.Should().ContainSingle();
        first.Next!.History.After.Should().Be(4);
        SessionCommandResult.Serialize(new("observed", SessionHistorySearchPage.Scope) { HistorySearch = first })
            .Length.Should().BeLessThanOrEqualTo(65536);
        var next = await f.Service.SearchHistoryAsync(f.Request.SessionId, "needle", first.Next, 50, f.Token);
        next.OmittedMatches.Should().Be(1);
        next.Records.Should().ContainSingle().Which.Sequence.Should().Be(6);
        next.Next.Should().BeNull();
    }

    [Theory]
    [InlineData("query")]
    [InlineData("cursor")]
    [InlineData("limit")]
    [InlineData("foreign-page")]
    [InlineData("foreign-record")]
    [InlineData("generation")]
    [InlineData("snapshot")]
    [InlineData("order")]
    [InlineData("ahead")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("cancel")]
    [InlineData("late-cancel")]
    public async Task Search_never_publishes_cross_session_invalid_stale_or_cancelled_reads(string mode)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var record = SearchRecord(f, mode is "ahead" ? 3 : 1);
        if (mode is "foreign-record") { record = record with { SessionId = new(Guid.NewGuid()) }; }
        f.HistoryReader = (session, _, _) => new(mode is "foreign-page" ? new(Guid.NewGuid()) : session,
            mode is "generation" ? new(2) : new(1), false, mode is "snapshot" ? 3 : 2,
            mode is "order" ? [record, record] : [record], null);
        var cursor = new SessionHistorySearchCursor(new(f.Request.SessionId, new(1), 2, 0),
            new SessionHistorySearch("needle").Digest);
        if (mode is "cursor") { cursor = cursor with { History = cursor.History with { SessionId = new(Guid.NewGuid()) } }; }
        if (mode is "privacy") { f.AfterHistoryRead = () => f.CanInspect = false; }
        if (mode is "revision") { f.AfterHistoryRead = f.AdvanceRevision; }
        using var cancelled = new CancellationTokenSource();
        if (mode is "cancel") { cancelled.Cancel(); }
        if (mode is "late-cancel") { f.AfterHistoryRead = cancelled.Cancel; }
        var act = () => f.Service.SearchHistoryAsync(f.Request.SessionId, mode is "query" ? "other" : "needle",
            cursor, mode is "limit" ? 0 : 50, cancelled.Token);
        await act.Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task Search_measures_exact_complete_JSON_byte_boundary(int extraByte, int expectedMatches)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var record = SearchRecord(f, 1);
        var next = new SessionHistorySearchCursor(new(f.Request.SessionId, new(1), 1, 1),
            new SessionHistorySearch("needle").Digest);
        var candidate = new SessionHistorySearchPage(f.Request.SessionId, new(1), false, 1, [record],
            200, 200, 200, next);
        var size = SessionCommandResult.GetSerializedSize(new("observed", SessionHistorySearchPage.Scope) { HistorySearch = candidate });
        record = record with { Question = record.Question + new string(' ', 65536 - size + extraByte) };
        SearchReader(f, [record]);
        var result = await f.Service.SearchHistoryAsync(f.Request.SessionId, "needle", null, 50, f.Token);
        result.Records.Should().HaveCount(expectedMatches);
        result.OmittedMatches.Should().Be(1 - expectedMatches);
        result.Next.Should().BeNull();
        SessionCommandResult.Serialize(new("observed", SessionHistorySearchPage.Scope) { HistorySearch = result }).Length
            .Should().BeLessThanOrEqualTo(65536);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Original_input_search_reuses_host_activity_without_query_content_or_authority_writes(RequestOrigin origin)
    {
        using var f = new Fixture();
        SearchReader(f, [SearchRecord(f, 1)]);
        using var hostile = new Activity("hostile").SetIdFormat(ActivityIdFormat.W3C).Start();
        hostile.AddTag("kora.session.id", Guid.NewGuid());
        var tags = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => tags.AddRange(activity.TagObjects.Select(pair => pair.Key + "=" + pair.Value)),
        };
        ActivitySource.AddActivityListener(listener);
        var command = new SessionCommand(SessionCommandOperation.HistorySearch, f.Request.SessionId.Value) { HistoryQuery = "needle" };
        (await f.Service.ExecuteCommandAsync(command, origin, () => true, f.Token)).HistorySearch!.SessionId.Should().Be(f.Request.SessionId);
        tags.Should().NotContain(value => value.Contains("needle", StringComparison.Ordinal));
        f.TaskWrites.Should().BeEmpty();
        f.Logger.Messages.Should().BeEmpty();
        var denied = () => f.Service.ExecuteCommandAsync(command, RequestOrigin.HostSystem, () => true, f.Token);
        await denied.Should().ThrowAsync<InvalidOperationException>();
        var missing = () => f.Service.ExecuteCommandAsync(command with { HistoryQuery = null }, origin, () => true, f.Token);
        await missing.Should().ThrowAsync<InvalidOperationException>();
        f.AfterHistoryRead = () => f.CanInspect = false;
        var late = () => f.Service.ExecuteCommandAsync(command, origin, () => true, f.Token);
        await late.Should().ThrowAsync<InvalidOperationException>();
    }
}
