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
