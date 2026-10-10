using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    private static SessionWorkspaceEntry ListEntry(int id, string? name = "needle", bool active = true)
    {
        var session = new HostId<SessionIdentity>(Guid.Parse("00000000-0000-0000-0000-" + id.ToString("D12", System.Globalization.CultureInfo.InvariantCulture)));
        return new(new(session, new(1), active), name is null ? null : new(session, new(1), new(name)));
    }

    private static void ListReader(Fixture f, ImmutableArray<SessionWorkspaceEntry> records)
    {
        f.ListReader = (after, limit) =>
        {
            var rows = records.Where(entry => string.CompareOrdinal(entry.Authority.SessionId.Value.ToString("D"),
                after?.ToString("D") ?? string.Empty) > 0).Take(limit + 1).ToArray();
            return new([.. rows.Take(limit)], rows.Length > limit ? rows[limit - 1].Authority.SessionId.Value : null);
        };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(51)]
    [InlineData(101)]
    public async Task ListSearchContinuesAfterZeroMatchesAcrossBoundedMetadataPages(int count)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        ListReader(f, [.. Enumerable.Range(1, count).Select(id => ListEntry(id, id == count ? "needle" : null))]);
        SessionListSearchCursor? cursor = null;
        var scanned = 0;
        var matches = 0;
        do
        {
            var result = await f.Service.SearchListAsync(SessionListSearchKind.NameSubstring, SessionListFilter.All,
                "needle", cursor, 50, f.Token);
            result.Scanned.Should().BeLessThanOrEqualTo(50);
            result.Unnamed.Should().Be(result.Scanned - result.Records.Length);
            if (result.Next is not null) { result.Next.After.Should().NotBe(Guid.Empty); }
            scanned += result.Scanned;
            matches += result.Records.Length;
            cursor = result.Next;
        } while (cursor is not null);
        scanned.Should().Be(count);
        matches.Should().Be(count == 0 ? 0 : 1);
        f.ListReads.Should().Be(Math.Max(1, (count + 49) / 50));
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    public async Task ListSearchStopsWithoutDroppingMatchingRowsAtTheRequestedLimit(int limit)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        ListReader(f, [.. Enumerable.Range(1, 51).Select(id => ListEntry(id))]);
        var first = await f.Service.SearchListAsync(SessionListSearchKind.NameSubstring, SessionListFilter.All, "needle", null, limit, f.Token);
        first.Records.Should().HaveCount(limit);
        first.Scanned.Should().Be(limit);
        first.OutputLimited.Should().BeTrue();
        first.Next!.After.Should().Be(ListEntry(limit).Authority.SessionId.Value);
        var next = await f.Service.SearchListAsync(SessionListSearchKind.NameSubstring, SessionListFilter.All, "needle", first.Next, 50, f.Token);
        next.Records.Select(entry => entry.Authority.SessionId).Should()
            .Equal(Enumerable.Range(limit + 1, 51 - limit).Select(id => ListEntry(id).Authority.SessionId));
        next.Next.Should().BeNull();
    }

    [Fact]
    public async Task CompleteByteBoundPreservesEveryLargeUnicodeMatchAcrossContinuations()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var name = string.Concat(Enumerable.Repeat("🐈", 120));
        ListReader(f, [.. Enumerable.Range(1, 50).Select(id => ListEntry(id, name))]);
        var first = await f.Service.SearchListAsync(SessionListSearchKind.NameSubstring, SessionListFilter.All, "🐈", null, 50, f.Token);
        first.Records.Length.Should().BeInRange(1, 49);
        first.OutputLimited.Should().BeTrue();
        first.Next.Should().NotBeNull();
        SessionListSearchPage.Serialize(first).Length.Should().BeLessThanOrEqualTo(65536);
        var next = await f.Service.SearchListAsync(SessionListSearchKind.NameSubstring, SessionListFilter.All, "🐈", first.Next, 50, f.Token);
        first.Records.Concat(next.Records).Select(entry => entry.Authority.SessionId).Should()
            .Equal(Enumerable.Range(1, 50).Select(id => ListEntry(id).Authority.SessionId));
        next.Next.Should().BeNull();
        SessionListSearchPage.Serialize(next).Length.Should().BeLessThanOrEqualTo(65536);
    }

    [Theory]
    [InlineData(SessionListFilter.All, 1)]
    [InlineData(SessionListFilter.Active, 0)]
    [InlineData(SessionListFilter.Done, 1)]
    public async Task ExactLookupIncludesUnnamedDoneMetadataWithoutScanningAnInventory(SessionListFilter filter, int count)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        f.ExactListReader = session => new(new(session, new(1), false), null);
        var result = await f.Service.SearchListAsync(SessionListSearchKind.ExactId, filter,
            f.Request.SessionId.Value.ToString("D"), null, 1, f.Token);
        result.Records.Should().HaveCount(count);
        result.Scanned.Should().Be(1);
        result.Unnamed.Should().Be(1);
        result.Next.Should().BeNull();
        result.OutputLimited.Should().BeFalse();
        f.ListReads.Should().Be(0);
        f.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task NamedExactLookupRetainsOnlyTheObservedImmutableSubjectAndMetadataRevisions()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = new SessionWorkspaceEntry(f.Session, new(f.Request.SessionId, new(2), new("Exact name")));
        f.ExactListReader = _ => entry;
        var result = await f.Service.SearchListAsync(SessionListSearchKind.ExactId, SessionListFilter.All,
            f.Request.SessionId.Value.ToString("D"), null, 1, f.Token);
        result.Records.Should().ContainSingle().Which.Should().Be(entry);
        result.Unnamed.Should().Be(0);
        f.ListReads.Should().Be(0);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("oversize")]
    [InlineData("empty-next")]
    [InlineData("zero-next")]
    [InlineData("wrong-next")]
    [InlineData("order")]
    [InlineData("before")]
    [InlineData("metadata-owner")]
    [InlineData("generation")]
    [InlineData("metadata-revision")]
    [InlineData("query")]
    [InlineData("limit")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("cancel")]
    [InlineData("late-cancel")]
    [InlineData("read")]
    public async Task InvalidUnavailableOrLateListReadsNeverPublishEmptySuccessOrWriteIntent(string mode)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var record = ListEntry(1);
        var page = new SessionPage<SessionWorkspaceEntry>([record], null);
        if (mode is "default") { page = new(default, null); }
        if (mode is "oversize") { page = new([.. Enumerable.Range(1, 51).Select(id => ListEntry(id))], null); }
        if (mode is "empty-next") { page = new([], record.Authority.SessionId.Value); }
        if (mode is "zero-next") { page = page with { Next = Guid.Empty }; }
        if (mode is "wrong-next") { page = page with { Next = Guid.NewGuid() }; }
        if (mode is "order") { page = new([record, record], null); }
        if (mode is "metadata-owner") { page = new([record with { Metadata = record.Metadata! with { SessionId = new(Guid.NewGuid()) } }], null); }
        if (mode is "generation") { page = new([record with { Authority = record.Authority with { Generation = default } }], null); }
        if (mode is "metadata-revision") { page = new([record with { Metadata = record.Metadata! with { Revision = default } }], null); }
        f.ListReader = (_, _) => page;
        if (mode is "privacy") { f.AfterListRead = () => f.CanInspect = false; }
        if (mode is "revision") { f.AfterListRead = f.AdvanceRevision; }
        if (mode is "read") { f.ListReader = (_, _) => throw new IOException("unavailable"); }
        using var cancel = new CancellationTokenSource();
        if (mode is "cancel") { cancel.Cancel(); }
        if (mode is "late-cancel") { f.AfterListRead = cancel.Cancel; }
        SessionListSearchCursor? cursor = null;
        if (mode is "before")
        {
            f.ListReader = (_, _) => new([record, ListEntry(2)], ListEntry(2).Authority.SessionId.Value);
            cursor = (await f.Service.SearchListAsync(SessionListSearchKind.NameSubstring, SessionListFilter.All, "needle", null, 1, f.Token)).Next;
            f.ListReader = (_, _) => page;
        }
        var act = () => f.Service.SearchListAsync(SessionListSearchKind.NameSubstring, SessionListFilter.All,
            mode is "query" ? "" : "needle", cursor, mode is "limit" ? 0 : 50, cancel.Token);
        await act.Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("filter")]
    [InlineData("host")]
    [InlineData("admission")]
    public async Task ContinuationCannotBeReusedForChangedQueryScopeHostOrAdmission(string mode)
    {
        using var f = new Fixture();
        using var other = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        ListReader(f, [ListEntry(1), ListEntry(2)]);
        var first = await f.Service.SearchListAsync(SessionListSearchKind.NameSubstring, SessionListFilter.All, "needle", null, 1, f.Token);
        if (mode is "admission") { f.AdvanceRevision(); }
        var act = () => (mode is "host" ? other.Service : f.Service).SearchListAsync(
            SessionListSearchKind.NameSubstring, mode is "filter" ? SessionListFilter.Done : SessionListFilter.All,
            mode is "query" ? "other" : "needle", first.Next, 1, f.Token);
        await act.Should().ThrowAsync<ArgumentException>();
        f.ListReads.Should().Be(1);
        other.ListReads.Should().Be(0);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("cancel")]
    [InlineData("unavailable")]
    public async Task ExactLookupFailsClosedOnForeignStorageLateCancellationOrUnknownId(string mode)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        using var cancel = new CancellationTokenSource();
        f.ExactListReader = _ =>
        {
            if (mode is "unavailable") { throw new InvalidOperationException("Unknown exact session."); }
            if (mode is "cancel") { cancel.Cancel(); }
            return mode is "foreign" ? ListEntry(1) : new(f.Session, null);
        };
        var act = () => f.Service.SearchListAsync(SessionListSearchKind.ExactId, SessionListFilter.All,
            f.Request.SessionId.Value.ToString("D"), null, 1, cancel.Token);
        await act.Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
    }
}
