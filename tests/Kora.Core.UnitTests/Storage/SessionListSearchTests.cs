using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionListSearchTests
{
    private static readonly Guid Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static SessionWorkspaceEntry Entry(string? name = "café I_100% 🐈", bool active = true, Guid? id = null) =>
        new(new(new(id ?? Id), new(1), active), name is null ? null : new(new(id ?? Id), new(1), new(name)));

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1.0")]
    [InlineData("1,000")]
    [InlineData("1e2")]
    [InlineData("١")]
    [InlineData("9223372036854775808")]
    public void EachNumericBoundRejectsInvalidInputWithoutFallback(string? text)
    {
        for (var index = 0; index < 4; index++)
        {
            var bounds = new[] { "", "", "", "" };
            bounds[index] = text!;
            SessionListSearch.IsValid(SessionListSearchKind.NameSubstring, SessionListFilter.All, "café",
                bounds[0], bounds[1], bounds[2], bounds[3]).Should().BeFalse();
        }
    }

    [Fact]
    public void PositiveInclusiveRangesHaveInvariantParsingOrderingAndSemanticDigests()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("ar-SA");
            var query = SessionListSearch.Parse(SessionListSearchKind.ExactId, SessionListFilter.All, Id.ToString("D"),
                "0001", long.MaxValue.ToString(CultureInfo.InvariantCulture), "1", "1");
            query.MinimumGeneration.Should().Be(1);
            query.MaximumGeneration.Should().Be(long.MaxValue);
            query.MinimumMetadataRevision.Should().Be(1);
            query.MaximumMetadataRevision.Should().Be(1);
            query.Matches(Entry()).Should().BeTrue();
            query.Digest.Should().Be(new SessionListSearch(query.Kind, query.Filter, query.Value, 1, long.MaxValue, 1, 1).Digest);
            SessionListSearch.IsValid(query.Kind, query.Filter, query.Value, "2", "1").Should().BeFalse();
            SessionListSearch.IsValid(query.Kind, query.Filter, query.Value, "", "", "2", "1").Should().BeFalse();
            for (var index = 0; index < 4; index++)
            {
                var bounds = new long?[] { null, null, null, null };
                bounds[index] = -1;
                var invalid = () => new SessionListSearch(query.Kind, query.Filter, query.Value,
                    bounds[0], bounds[1], bounds[2], bounds[3]);
                invalid.Should().Throw<InvalidDataException>();
                bounds[index] = 1;
                var changed = new SessionListSearch(query.Kind, query.Filter, query.Value,
                    bounds[0], bounds[1], bounds[2], bounds[3]);
                var unfiltered = new SessionListSearch(query.Kind, query.Filter, query.Value);
                changed.Digest.Should().NotBe(unfiltered.Digest);
                var cursor = new SessionListSearchCursor(Id, 1, Id, unfiltered.Digest);
                var validate = () => cursor.Validate(Id, 1, changed);
                validate.Should().Throw<ArgumentException>();
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(null, null, null, null, true)]
    [InlineData(2L, null, null, null, true)]
    [InlineData(3L, null, null, null, false)]
    [InlineData(null, 2L, null, null, true)]
    [InlineData(null, 1L, null, null, false)]
    [InlineData(null, null, 2L, null, true)]
    [InlineData(null, null, 3L, null, false)]
    [InlineData(null, null, null, 2L, true)]
    [InlineData(null, null, null, 1L, false)]
    [InlineData(2L, 2L, 2L, 2L, true)]
    public void RangesComposeWithNameExactIdAndLifecycleAndNeverInventUnnamedRevisions(
        long? minimum, long? maximum, long? revisionMinimum, long? revisionMaximum, bool match)
    {
        var row = Entry() with { Authority = Entry().Authority with { Generation = new(2) },
            Metadata = Entry().Metadata! with { Revision = new(2) } };
        foreach (var kind in Enum.GetValues<SessionListSearchKind>())
        {
            var value = kind is SessionListSearchKind.ExactId ? Id.ToString("D") : "café";
            var query = new SessionListSearch(kind, SessionListFilter.Active, value,
                minimum, maximum, revisionMinimum, revisionMaximum);
            query.Matches(row).Should().Be(match);
            query.Matches(row with { Authority = row.Authority with { IsActive = false } }).Should().BeFalse();
            query.Matches(row with { Metadata = null }).Should().Be(
                match && kind is SessionListSearchKind.ExactId && revisionMinimum is null && revisionMaximum is null);
            var wrongValue = kind is SessionListSearchKind.ExactId ? Guid.NewGuid().ToString("D") : "absent";
            new SessionListSearch(kind, SessionListFilter.All, wrongValue, minimum, maximum, revisionMinimum, revisionMaximum)
                .Matches(row).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("authority")]
    [InlineData("id")]
    [InlineData("generation")]
    [InlineData("owner")]
    [InlineData("revision")]
    [InlineData("name")]
    public void CompleteCandidatesAreValidatedEvenWhenOtherPredicatesExcludeThem(string mode)
    {
        var row = Entry(active: false);
        if (mode is "null") { row = null!; }
        if (mode is "authority") { row = row with { Authority = null! }; }
        if (mode is "id") { row = row with { Authority = row.Authority with { SessionId = default } }; }
        if (mode is "generation") { row = row with { Authority = row.Authority with { Generation = default } }; }
        if (mode is "owner") { row = row with { Metadata = row.Metadata! with { SessionId = new(Guid.NewGuid()) } }; }
        if (mode is "revision") { row = row with { Metadata = row.Metadata! with { Revision = default } }; }
        if (mode is "name") { row = row with { Metadata = row.Metadata! with { Name = null! } }; }
        var query = new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.Active, "absent", 50, 100, 50, 100);
        var act = () => query.Matches(row);
        act.Should().Throw<Exception>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" cafe")]
    [InlineData("cafe ")]
    [InlineData("cafe\u0301")]
    [InlineData("a\nb")]
    [InlineData("a\u200bb")]
    public void InvalidNamesAreDeniedWithoutNormalizationOrUnfilteredFallback(string? query)
    {
        SessionListSearch.IsValid(SessionListSearchKind.NameSubstring, SessionListFilter.All, query!).Should().BeFalse();
        var act = () => new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.All, query!);
        act.Should().Throw<Exception>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("AAAAAAAA-0000-0000-0000-000000000001")]
    [InlineData("aaaaaaaa000000000000000000000001")]
    [InlineData("{aaaaaaaa-0000-0000-0000-000000000001}")]
    [InlineData(" aaaaaaaa-0000-0000-0000-000000000001")]
    [InlineData("café")]
    public void ExactIdsRequireNonemptyCanonicalLowercaseDFormat(string query) =>
        SessionListSearch.IsValid(SessionListSearchKind.ExactId, SessionListFilter.All, query).Should().BeFalse();

    [Theory]
    [InlineData("café", true)]
    [InlineData("CAFÉ", false)]
    [InlineData("I", true)]
    [InlineData("ı", false)]
    [InlineData("_100%", true)]
    [InlineData("%", true)]
    [InlineData("🐈", true)]
    [InlineData("absent", false)]
    public void NameMatchingIsLiteralOrdinalAndCultureIndependent(string query, bool matches)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("tr-TR");
            var search = new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.All, query);
            search.Matches(Entry()).Should().Be(matches);
            search.Matches(Entry(null)).Should().BeFalse();
            search.Value.Should().Be(query);
            search.Kind.Should().Be(SessionListSearchKind.NameSubstring);
            search.Filter.Should().Be(SessionListFilter.All);
            search.ExactId.Should().BeNull();
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(SessionListFilter.All, true, true)]
    [InlineData(SessionListFilter.All, false, true)]
    [InlineData(SessionListFilter.Active, true, true)]
    [InlineData(SessionListFilter.Active, false, false)]
    [InlineData(SessionListFilter.Done, false, true)]
    [InlineData(SessionListFilter.Done, true, false)]
    public void ExactIdAndNameApplyTheExplicitStateScope(SessionListFilter filter, bool active, bool matches)
    {
        var search = new SessionListSearch(SessionListSearchKind.ExactId, filter, Id.ToString("D"));
        search.ExactId.Should().Be(Id);
        search.Matches(Entry(null, active)).Should().Be(matches);
        search.Matches(Entry(Id.ToString("D"), active, Guid.NewGuid())).Should().BeFalse();
        new SessionListSearch(SessionListSearchKind.NameSubstring, filter, "café").Matches(Entry(active: active))
            .Should().Be(matches);
    }

    [Fact]
    public void QueryBoundsAndDigestsUseTheAuthoritativeNameDomain()
    {
        var valid = string.Concat(Enumerable.Repeat("🐈", 120));
        SessionListSearch.IsValid(SessionListSearchKind.NameSubstring, SessionListFilter.All, valid).Should().BeTrue();
        SessionListSearch.IsValid(SessionListSearchKind.NameSubstring, SessionListFilter.All, valid + "a").Should().BeFalse();
        SessionListSearch.IsValid(SessionListSearchKind.NameSubstring, SessionListFilter.All, new('a', 121)).Should().BeFalse();
        SessionListSearch.IsValid(SessionListSearchKind.NameSubstring, SessionListFilter.All, new('界', 161)).Should().BeFalse();
        SessionListSearch.IsValid(SessionListSearchKind.NameSubstring, SessionListFilter.All, new('a', 100000)).Should().BeFalse();
        SessionListSearch.IsValid(SessionListSearchKind.NameSubstring, SessionListFilter.All, new string((char)0xd800, 1)).Should().BeFalse();
        SessionListSearch.IsValid((SessionListSearchKind)99, SessionListFilter.All, "x").Should().BeFalse();
        SessionListSearch.IsValid(SessionListSearchKind.NameSubstring, (SessionListFilter)99, "x").Should().BeFalse();
        var name = new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.All, Id.ToString("D"));
        name.Digest.Should().NotBe(new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, Id.ToString("D")).Digest);
        name.Digest.Should().NotBe(new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.Done, name.Value).Digest);
        name.Digest.Should().NotBe(new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.All, name.Value.ToUpperInvariant()).Digest);
        name.Digest.Should().Be(new SessionListSearch(name.Kind, name.Filter, name.Value).Digest);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("host")]
    [InlineData("empty-host")]
    [InlineData("revision")]
    [InlineData("empty-after")]
    [InlineData("query")]
    [InlineData("kind")]
    [InlineData("null")]
    public void CursorsAreBoundToQueryStateHostAndAdmission(string mode)
    {
        var host = Guid.NewGuid();
        var search = new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.All, "café");
        var cursor = new SessionListSearchCursor(host, 1, Id, search.Digest);
        cursor.Host.Should().Be(host);
        cursor.AdmissionRevision.Should().Be(1);
        cursor.After.Should().Be(Id);
        cursor.QueryDigest.Should().Be(search.Digest);
        if (mode is "host") { cursor = cursor with { Host = Guid.NewGuid() }; }
        if (mode is "empty-host") { cursor = cursor with { Host = Guid.Empty }; }
        if (mode is "revision") { cursor = cursor with { AdmissionRevision = 2 }; }
        if (mode is "empty-after") { cursor = cursor with { After = Guid.Empty }; }
        if (mode is "query") { cursor = cursor with { QueryDigest = string.Empty }; }
        if (mode is "kind") { search = new(SessionListSearchKind.ExactId, SessionListFilter.All, Id.ToString("D")); cursor = cursor with { QueryDigest = search.Digest }; }
        var act = () => cursor.Validate(host, 1, mode is "null" ? null! : search);
        if (mode is "valid") { act.Should().NotThrow(); }
        else { act.Should().Throw<ArgumentException>(); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void ResultsEnforceRecordAndCompleteByteBounds(int limit, bool valid)
    {
        var validate = () => SessionListSearchPage.ValidateLimit(limit);
        if (valid) { validate.Should().NotThrow(); }
        else { validate.Should().Throw<ArgumentOutOfRangeException>(); }
        var record = Entry(string.Concat(Enumerable.Repeat("🐈", 120)));
        var page = new SessionListSearchPage([record], 1, 0, false, null);
        SessionListSearchPage.Serialize(page).Length.Should().Be(SessionListSearchPage.GetSerializedSize(page));
        page.Scanned.Should().Be(1);
        page.Unnamed.Should().Be(0);
        page.OutputLimited.Should().BeFalse();
        page.Next.Should().BeNull();
        page = page with { Records = [.. Enumerable.Repeat(record, 50)] };
        SessionListSearchPage.GetSerializedSize(page).Should().BeGreaterThan(65536);
        var serialize = () => SessionListSearchPage.Serialize(page);
        serialize.Should().Throw<InvalidDataException>();
    }
}
