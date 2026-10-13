using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SavedDraftCatalogueTests
{
    [Theory]
    [InlineData("source")]
    [InlineData("revision")]
    [InlineData("bytes-zero")]
    [InlineData("bytes-max")]
    [InlineData("scalars-zero")]
    [InlineData("scalars-max")]
    [InlineData("control")]
    [InlineData("ceiling")]
    [InlineData("digest-null")]
    [InlineData("digest-length")]
    [InlineData("digest-shape")]
    [InlineData("foreign-name")]
    public void SummaryRejectsUnavailableMetadata(string boundary)
    {
        var row = Row(1);
        row = boundary switch
        {
            "source" => row with { Source = Guid.Empty },
            "revision" => row with { DraftRevision = 0 },
            "bytes-zero" => row with { Utf8Bytes = 0 },
            "bytes-max" => row with { Utf8Bytes = 16385 },
            "scalars-zero" => row with { UnicodeScalars = 0 },
            "scalars-max" => row with { UnicodeScalars = 4097 },
            "control" => row with { ControlRevision = -1 },
            "ceiling" => row with { AuthorityCeiling = 0 },
            "digest-null" => row with { AuthorityDigest = null! },
            "digest-length" => row with { AuthorityDigest = "abc" },
            "digest-shape" => row with { AuthorityDigest = new string('g', 64) },
            _ => row with { Metadata = new(new(Guid.NewGuid()), new(1), new("foreign")) },
        };
        row.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("default")]
    [InlineData("count")]
    [InlineData("scanned-small")]
    [InlineData("scanned-max")]
    [InlineData("audit-small")]
    [InlineData("audit-max")]
    [InlineData("pending-next")]
    [InlineData("pending-scanned")]
    [InlineData("pending-record")]
    [InlineData("zero-next")]
    [InlineData("order")]
    [InlineData("filter-active")]
    [InlineData("filter-done")]
    [InlineData("source")]
    [InlineData("profile")]
    [InlineData("control")]
    [InlineData("ceiling")]
    [InlineData("digest")]
    [InlineData("query-kind")]
    [InlineData("query-missing-digest")]
    [InlineData("query-missing-kind")]
    [InlineData("query-digest-length")]
    [InlineData("query-digest-shape")]
    public void PageRejectsInvalidOrMixedScope(string boundary)
    {
        var first = Row(1);
        var second = Row(2);
        var page = new SavedDraftPage(SessionListFilter.All, [first, second], null, 2, 1, false);
        page = boundary switch
        {
            "filter" => page with { Filter = (SessionListFilter)99 },
            "default" => page with { Records = default },
            "count" => page with { Records = Enumerable.Repeat(first, 26).ToImmutableArray() },
            "scanned-small" => page with { Scanned = 1 },
            "scanned-max" => page with { Scanned = 51 },
            "audit-small" => page with { AuditScanned = -1 },
            "audit-max" => page with { AuditScanned = 4097 },
            "pending-next" => page with { AuthorityPending = true },
            "pending-scanned" => page with { AuthorityPending = true, Next = Cursor(), Records = [] },
            "pending-record" => page with { AuthorityPending = true, Next = Cursor(), Scanned = 0 },
            "zero-next" => page with { Records = [], Scanned = 0, Next = Cursor() },
            "order" => page with { Records = [second, first] },
            "filter-active" => page with { Filter = SessionListFilter.Active, Records = [first with { Session = first.Session with { IsActive = false } }] },
            "filter-done" => page with { Filter = SessionListFilter.Done },
            "source" => page with { Records = [first, second with { Source = Guid.NewGuid() }] },
            "profile" => page with { Records = [first, second with { Profile = new(Guid.NewGuid()) }] },
            "control" => page with { Records = [first, second with { ControlRevision = 2 }] },
            "ceiling" => page with { Records = [first, second with { AuthorityCeiling = 2 }] },
            "query-kind" => page with { QueryKind = (SessionListSearchKind)99, QueryDigest = new string('a', 64) },
            "query-missing-digest" => page with { QueryKind = SessionListSearchKind.NameSubstring },
            "query-missing-kind" => page with { QueryDigest = new string('a', 64) },
            "query-digest-length" => page with { QueryKind = SessionListSearchKind.ExactId, QueryDigest = "a" },
            "query-digest-shape" => page with { QueryKind = SessionListSearchKind.ExactId, QueryDigest = new string('A', 64) },
            _ => page with { Records = [first, second with { AuthorityDigest = new string('b', 64) }] },
        };
        page.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void CompleteWorstCaseProjectionFitsWithoutTruncationAndEmptyContinuationIsNotEnd()
    {
        var rows = Enumerable.Range(1, 25).Select(index =>
        {
            var row = Row(index);
            var name = string.Concat(Enumerable.Repeat("🙂", SessionName.MaximumScalars));
            return row with { Metadata = new(row.Session.SessionId, new(long.MaxValue), new(name)),
                Session = row.Session with { Generation = new(long.MaxValue) }, DraftRevision = long.MaxValue,
                Utf8Bytes = 16384, UnicodeScalars = 4096, ControlRevision = long.MaxValue, AuthorityCeiling = long.MaxValue };
        }).ToImmutableArray();
        var query = new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.Active, "🙂");
        var page = new SavedDraftPage(SessionListFilter.Active, rows, Cursor(3000), 50, 4096, false, query.Kind, query.Digest);
        page.Validate();
        SavedDraftPage.Size(page).Should().BeLessThanOrEqualTo(SavedDraftPage.MaximumBytes);
        page.Records.Should().HaveCount(25);
        new SavedDraftPage(SessionListFilter.Done, [Row(1) with { Session = Row(1).Session with { IsActive = false } }],
            null, 1, 1, false).Validate();
        new SavedDraftPage(SessionListFilter.All, [], Cursor(), 50, 0, false).Validate();
        new SavedDraftPage(SessionListFilter.All, [], Cursor(), 0, 4096, true).Validate();
        new SavedDraftPage(SessionListFilter.All, [], null, 0, 0, false).Validate();
        Row(1).Validate();
        (Row(1) with { Metadata = new(Row(1).Session.SessionId, new(1), new("private inert name")) }).Validate();
        SavedDraftPage.Scope.Should().Contain("not an atomic").And.Contain("never reloads");
    }

    private static string Cursor(int bytes = 1) => Convert.ToBase64String(new byte[bytes]) + "." + new string('a', 64);
    private static SavedDraftSummary Row(int id) => new(new("10000000-0000-0000-0000-000000000001"),
        new(new Guid("20000000-0000-0000-0000-000000000001")),
        new(new(Guid.Parse("30000000-0000-0000-0000-" + id.ToString("D12", System.Globalization.CultureInfo.InvariantCulture))), new(1), true),
        null, 1, 4, 1, 1, 1, new string('a', 64));
}
