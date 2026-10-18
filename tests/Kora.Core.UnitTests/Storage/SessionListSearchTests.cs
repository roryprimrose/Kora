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
    [InlineData("a", 1)]
    [InlineData("é", 1)]
    [InlineData("\U0001F408", 1)]
    [InlineData("日本語", 3)]
    [InlineData("q\u0301", 2)]
    [InlineData("\U00010400é", 2)]
    public void NameLengthIsInclusiveAndComposesWithEveryModeLifecycleAvailabilityAndRevision(string name, long count)
    {
        foreach (var kind in Enum.GetValues<SessionListSearchKind>())
        foreach (var filter in Enum.GetValues<SessionListFilter>())
        foreach (var availability in Enum.GetValues<SessionNameAvailability>())
        foreach (var active in new[] { true, false })
        foreach (var revisionBound in new[] { false, true })
        foreach (var lengths in new (long? Minimum, long? Maximum, bool Matches)[]
            { (null, null, true), (count, null, true), (null, count, true),
              (count, count, true), (count + 1, null, false), (null, count - 1 == 0 ? null : count - 1, count == 1) })
        {
            var value = kind is SessionListSearchKind.Browse ? ""
                : kind is SessionListSearchKind.ExactId ? Id.ToString("D") : name;
            var query = new SessionListSearch(kind, filter, value, 1, 1,
                revisionBound ? 1 : null, revisionBound ? 1 : null, availability, lengths.Minimum, lengths.Maximum);
            var lifecycle = filter is SessionListFilter.All || active == (filter is SessionListFilter.Active);
            query.Matches(Entry(name, active)).Should().Be(lifecycle && lengths.Matches
                && availability is not SessionNameAvailability.Unnamed);
            query.Matches(Entry(null, active)).Should().Be(lifecycle && !revisionBound
                && lengths.Minimum is null && lengths.Maximum is null
                && availability is not SessionNameAvailability.Named
                && kind is not (SessionListSearchKind.NameSubstring or SessionListSearchKind.ExactName));
        }
    }

    [Fact]
    public void MaximumScalarNamesAndSemanticLengthDigestsReuseAuthoritativeAdmission()
    {
        var maximum = string.Concat(Enumerable.Repeat("\U0001F408", SessionName.MaximumScalars));
        var query = SessionListSearch.Parse(SessionListSearchKind.Browse, SessionListFilter.All, "",
            minimumNameLength: "0001", maximumNameLength: SessionName.MaximumScalars.ToString(CultureInfo.InvariantCulture));
        query.MinimumNameLength.Should().Be(1);
        query.MaximumNameLength.Should().Be(SessionName.MaximumScalars);
        query.Matches(Entry(maximum)).Should().BeTrue();
        query.Digest.Should().Be(new SessionListSearch(query.Kind, query.Filter, "",
            minimumNameLength: 1, maximumNameLength: SessionName.MaximumScalars).Digest);
        var exact = new SessionListSearch(query.Kind, query.Filter, "",
            minimumNameLength: SessionName.MaximumScalars, maximumNameLength: SessionName.MaximumScalars);
        exact.Matches(Entry(maximum)).Should().BeTrue();
        exact.Matches(Entry(maximum[..^2])).Should().BeFalse();
        var unfiltered = new SessionListSearch(query.Kind, query.Filter, "");
        unfiltered.MinimumNameLength.Should().BeNull();
        unfiltered.MaximumNameLength.Should().BeNull();
        unfiltered.Digest.Should().Be(SessionListSearch.Parse(query.Kind, query.Filter, "").Digest);
        foreach (var changed in new[] { query, exact,
            new SessionListSearch(query.Kind, query.Filter, "", minimumNameLength: 1),
            new SessionListSearch(query.Kind, query.Filter, "", maximumNameLength: 1) })
        {
            changed.Digest.Should().NotBe(unfiltered.Digest);
            var cursor = new SessionListSearchCursor(Id, 1, Id, unfiltered.Digest);
            var validate = () => cursor.Validate(Id, 1, changed);
            validate.Should().Throw<ArgumentException>();
        }
        new SessionListSearch(query.Kind, query.Filter, "", minimumNameLength: 1).Digest.Should()
            .NotBe(new SessionListSearch(query.Kind, query.Filter, "", maximumNameLength: 1).Digest);
        SessionListSearch.IsValid(query.Kind, query.Filter, "", minimumNameLength: "2", maximumNameLength: "1").Should().BeFalse();
        SessionListSearch.IsValid(query.Kind, query.Filter, "", minimumNameLength: "1", maximumNameLength: "1").Should().BeTrue();
    }

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
    [InlineData("１２")]
    [InlineData("9223372036854775808")]
    public void EachNameLengthTextBoundRejectsInvalidInputExplicitly(string? text)
    {
        foreach (var minimum in new[] { true, false })
        {
            SessionListSearch.IsValid(SessionListSearchKind.Browse, SessionListFilter.All, "",
                minimumNameLength: minimum ? text! : "", maximumNameLength: minimum ? "" : text!).Should().BeFalse();
            var parse = () => SessionListSearch.Parse(SessionListSearchKind.Browse, SessionListFilter.All, "",
                minimumNameLength: minimum ? text! : "", maximumNameLength: minimum ? "" : text!);
            parse.Should().Throw<Exception>();
        }
    }

    [Fact]
    public void TypedNameLengthBoundsRejectZeroNegativeBeyondMaximumAndInversion()
    {
        foreach (var invalid in new long[] { -1, 0, SessionName.MaximumScalars + 1, long.MaxValue })
        foreach (var minimum in new[] { true, false })
        {
            var create = () => new SessionListSearch(SessionListSearchKind.Browse, SessionListFilter.All, "",
                minimumNameLength: minimum ? invalid : null, maximumNameLength: minimum ? null : invalid);
            create.Should().Throw<InvalidDataException>();
        }
        var inverted = () => new SessionListSearch(SessionListSearchKind.Browse, SessionListFilter.All, "",
            minimumNameLength: 2, maximumNameLength: 1);
        inverted.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("needle")]
    [InlineData("aaaaaaaa-0000-0000-0000-000000000001")]
    public void BrowseRequiresExplicitEmptyTextAndNeverReinterpretsIt(string? text)
    {
        SessionListSearch.IsValid(SessionListSearchKind.Browse, SessionListFilter.All, text!).Should().BeFalse();
        var read = () => new SessionListSearch(SessionListSearchKind.Browse, SessionListFilter.All, text!);
        read.Should().Throw<Exception>();
    }

    [Fact]
    public void AvailabilityComposesWithEveryModeLifecycleAndRangeWithoutInventingUnnamedMetadata()
    {
        foreach (var kind in Enum.GetValues<SessionListSearchKind>())
        foreach (var filter in Enum.GetValues<SessionListFilter>())
        foreach (var availability in Enum.GetValues<SessionNameAvailability>())
        foreach (var active in new[] { true, false })
        foreach (var named in new[] { true, false })
        foreach (var revisionBound in new[] { false, true })
        {
            var text = kind is SessionListSearchKind.Browse ? ""
                : kind is SessionListSearchKind.ExactId ? Id.ToString("D") : "café";
            var query = SessionListSearch.Parse(kind, filter, text, "1", "1",
                revisionBound ? "1" : "", revisionBound ? "1" : "", availability);
            query.Value.Should().Be(text);
            query.NameAvailability.Should().Be(availability);
            query.ExactId.Should().Be(kind is SessionListSearchKind.ExactId ? Id : null);
            var expected = (filter is SessionListFilter.All || active == (filter is SessionListFilter.Active))
                && (availability is SessionNameAvailability.Any || named == (availability is SessionNameAvailability.Named))
                && (!revisionBound || named)
                && (kind is not (SessionListSearchKind.NameSubstring or SessionListSearchKind.ExactName) || named);
            query.Matches(Entry(named ? "café" : null, active)).Should().Be(expected);
            query.Matches(Entry(named ? "café" : null, active) with
                { Authority = Entry().Authority with { Generation = new(2), IsActive = active } }).Should().BeFalse();
        }
        SessionListSearch.IsValid(SessionListSearchKind.Browse, SessionListFilter.All, "",
            nameAvailability: (SessionNameAvailability)99).Should().BeFalse();
    }

    [Fact]
    public void BrowseAndEveryAvailabilityHaveDistinctSemanticCursorDigests()
    {
        var browse = new SessionListSearch(SessionListSearchKind.Browse, SessionListFilter.All, "");
        browse.Digest.Should().Be(new SessionListSearch(browse.Kind, browse.Filter, "").Digest);
        var cursor = new SessionListSearchCursor(Id, 1, Id, browse.Digest);
        cursor.Validate(Id, 1, browse);
        foreach (var changed in new[]
        {
            new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.All, "café"),
            new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, Id.ToString("D")),
            new SessionListSearch(SessionListSearchKind.Browse, SessionListFilter.Done, ""),
            new SessionListSearch(SessionListSearchKind.Browse, SessionListFilter.All, "", minimumGeneration: 1),
            new SessionListSearch(SessionListSearchKind.Browse, SessionListFilter.All, "", maximumGeneration: 1),
            new SessionListSearch(SessionListSearchKind.Browse, SessionListFilter.All, "", minimumMetadataRevision: 1),
            new SessionListSearch(SessionListSearchKind.Browse, SessionListFilter.All, "", maximumMetadataRevision: 1),
            new SessionListSearch(SessionListSearchKind.Browse, SessionListFilter.All, "", nameAvailability: SessionNameAvailability.Named),
            new SessionListSearch(SessionListSearchKind.Browse, SessionListFilter.All, "", nameAvailability: SessionNameAvailability.Unnamed)
        })
        {
            changed.Digest.Should().NotBe(browse.Digest);
            var validate = () => cursor.Validate(Id, 1, changed);
            validate.Should().Throw<ArgumentException>();
        }
        new SessionListSearch(browse.Kind, browse.Filter, "", nameAvailability: SessionNameAvailability.Named).Digest.Should()
            .NotBe(new SessionListSearch(browse.Kind, browse.Filter, "", nameAvailability: SessionNameAvailability.Unnamed).Digest);
    }

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
            var value = kind is SessionListSearchKind.Browse ? "" : kind is SessionListSearchKind.ExactId ? Id.ToString("D")
                : kind is SessionListSearchKind.ExactName ? row.Metadata!.Name.Value : "café";
            var query = new SessionListSearch(kind, SessionListFilter.Active, value,
                minimum, maximum, revisionMinimum, revisionMaximum);
            query.Matches(row).Should().Be(match);
            query.Matches(row with { Authority = row.Authority with { IsActive = false } }).Should().BeFalse();
            query.Matches(row with { Metadata = null }).Should().Be(
                match && kind is not (SessionListSearchKind.NameSubstring or SessionListSearchKind.ExactName)
                    && revisionMinimum is null && revisionMaximum is null);
            if (kind is SessionListSearchKind.Browse) { continue; }
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
        foreach (var kind in Enum.GetValues<SessionListSearchKind>())
        {
            var value = kind is SessionListSearchKind.Browse ? "" : kind is SessionListSearchKind.ExactId ? Id.ToString("D") : "absent";
            var query = new SessionListSearch(kind, SessionListFilter.Active, value, 50, 100, 50, 100,
                SessionNameAvailability.Unnamed, 1, 1);
            var act = () => query.Matches(row);
            act.Should().Throw<Exception>();
        }
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
        foreach (var kind in new[] { SessionListSearchKind.NameSubstring, SessionListSearchKind.ExactName })
        {
            SessionListSearch.IsValid(kind, SessionListFilter.All, query!).Should().BeFalse();
            var act = () => new SessionListSearch(kind, SessionListFilter.All, query!);
            act.Should().Throw<Exception>();
        }
    }

    [Theory]
    [InlineData("café I_100% 🐈", true)]
    [InlineData("CAFÉ I_100% 🐈", false)]
    [InlineData("café ı_100% 🐈", false)]
    [InlineData("café", false)]
    [InlineData("%", false)]
    [InlineData("🐈", false)]
    [InlineData("prefix café I_100% 🐈", false)]
    [InlineData("café I_100% 🐈 suffix", false)]
    public void ExactNameRequiresWholeOrdinalEqualityWithoutSupplyingIdentity(string value, bool matches)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("tr-TR");
            var search = new SessionListSearch(SessionListSearchKind.ExactName, SessionListFilter.All, value);
            search.Matches(Entry()).Should().Be(matches);
            search.Matches(Entry(id: Guid.NewGuid())).Should().Be(matches, "duplicate names do not identify a session");
            search.Matches(Entry(null)).Should().BeFalse();
            search.ExactId.Should().BeNull();
            search.Value.Should().Be(value);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void ExactNameCursorBindsKindValueAndEveryPredicateWithoutIdInterpretation()
    {
        var query = new SessionListSearch(SessionListSearchKind.ExactName, SessionListFilter.All, Id.ToString("D"));
        query.ExactId.Should().BeNull();
        query.Matches(Entry(Id.ToString("D"), id: Guid.NewGuid())).Should().BeTrue();
        query.Matches(Entry()).Should().BeFalse();
        var cursor = new SessionListSearchCursor(Id, 1, Id, query.Digest);
        cursor.Validate(Id, 1, new(query.Kind, query.Filter, query.Value));
        foreach (var changed in new[]
        {
            new SessionListSearch(SessionListSearchKind.NameSubstring, query.Filter, query.Value),
            new SessionListSearch(SessionListSearchKind.ExactId, query.Filter, query.Value),
            new SessionListSearch(SessionListSearchKind.Browse, query.Filter, ""),
            new SessionListSearch(query.Kind, query.Filter, "another"),
            new SessionListSearch(query.Kind, SessionListFilter.Active, query.Value),
            new SessionListSearch(query.Kind, query.Filter, query.Value, minimumGeneration: 1),
            new SessionListSearch(query.Kind, query.Filter, query.Value, maximumGeneration: 1),
            new SessionListSearch(query.Kind, query.Filter, query.Value, minimumMetadataRevision: 1),
            new SessionListSearch(query.Kind, query.Filter, query.Value, maximumMetadataRevision: 1),
            new SessionListSearch(query.Kind, query.Filter, query.Value, nameAvailability: SessionNameAvailability.Named),
            new SessionListSearch(query.Kind, query.Filter, query.Value, nameAvailability: SessionNameAvailability.Unnamed)
        })
        {
            changed.Digest.Should().NotBe(query.Digest);
            var validate = () => cursor.Validate(Id, 1, changed);
            validate.Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void ExactNameAdmitsOnlyCanonicalNamesWithinScalarAndByteBounds()
    {
        var maximum = string.Concat(Enumerable.Repeat("🐈", 120));
        SessionListSearch.IsValid(SessionListSearchKind.ExactName, SessionListFilter.All, maximum).Should().BeTrue();
        foreach (var value in new[] { maximum + "a", new string('a', 121), new string('a', 100000),
            new string((char)0xd800, 1), "a\u2028b", "a\u2029b", "a\0b" })
        {
            SessionListSearch.IsValid(SessionListSearchKind.ExactName, SessionListFilter.All, value).Should().BeFalse();
        }
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
