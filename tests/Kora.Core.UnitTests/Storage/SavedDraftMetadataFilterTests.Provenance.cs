using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed partial class SavedDraftMetadataFilterTests
{
    private static SavedDraftSummary ProvenanceRow()
    {
        var session = new Kora.Core.Authorization.WorkSessionAuthorization(new(Guid.NewGuid()), new(2), true);
        return new(Guid.Parse(SourceId), new(Guid.Parse(ProfileId)), session,
            new(session.SessionId, new(3), new("private name")), 2, 4, 1, 4, 5, new string('a', 64));
    }

    private static SavedDraftMetadataFilter ProvenanceFilter(long?[] values) =>
        new(2, 4, 4, 1, 1, Guid.Parse(SourceId), new(Guid.Parse(ProfileId)),
            values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]);

    private static SavedDraftMetadataFilter ParseProvenance(string[] values) =>
        SavedDraftMetadataFilter.Parse("2", "4", "4", "1", "1", SourceId, ProfileId,
            values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]);

    private static bool ValidProvenance(string[] values) =>
        SavedDraftMetadataFilter.IsValid("2", "4", "4", "1", "1", SourceId, ProfileId,
            values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]);

    [Fact]
    public void EveryProvenanceBoundIsStrictOptionalDecimalWithItsAuthoritativeFloor()
    {
        for (var field = 0; field < 8; field++)
        {
            foreach (var invalid in new[] { " ", " 1", "1 ", "+1", "-1", "1,000", "1_000",
                "1.0", "1e2", "1\n", "1\0", "１", "١", "\uD800", "9223372036854775808" })
            {
                var text = Enumerable.Repeat("", 8).ToArray();
                text[field] = invalid;
                ValidProvenance(text).Should().BeFalse();
                var parse = () => ParseProvenance(text);
                parse.Should().Throw<InvalidDataException>();
            }
            foreach (var value in new[] { "", "1", "0001", "9223372036854775807", "0" })
            {
                var text = Enumerable.Repeat("", 8).ToArray();
                text[field] = value;
                var valid = !string.Equals(value, "0", StringComparison.Ordinal) || field is 4 or 5;
                ValidProvenance(text).Should().Be(valid);
                if (valid)
                {
                    var filter = ParseProvenance(text);
                    JsonSerializer.Deserialize<SavedDraftMetadataFilter>(JsonSerializer.Serialize(filter)).Should().Be(filter);
                    var canonical = (string[])text.Clone();
                    if (string.Equals(value, "0001", StringComparison.Ordinal)) { canonical[field] = "1"; }
                    ParseProvenance(canonical).Digest.Should().Be(filter.Digest);
                }
            }
            var nulls = Enumerable.Repeat("", 8).ToArray();
            nulls[field] = null!;
            var parseNull = () => ParseProvenance(nulls);
            parseNull.Should().Throw<ArgumentNullException>();
            var negative = new long?[8];
            negative[field] = -1;
            var create = () => ProvenanceFilter(negative);
            create.Should().Throw<InvalidDataException>();
        }
        for (var pair = 0; pair < 8; pair += 2)
        {
            var bounds = new long?[8];
            bounds[pair] = 2;
            bounds[pair + 1] = 1;
            var create = () => ProvenanceFilter(bounds);
            create.Should().Throw<InvalidDataException>();
            var text = Enumerable.Repeat("", 8).ToArray();
            text[pair] = "2";
            text[pair + 1] = "1";
            ValidProvenance(text).Should().BeFalse();
        }
    }

    [Fact]
    public void EveryInclusiveBoundAndDigestComposesWithAllExistingPredicates()
    {
        var row = ProvenanceRow();
        var digests = new HashSet<string>(StringComparer.Ordinal) { ProvenanceFilter(new long?[8]).Digest };
        long[] actual = [2, 2, 3, 3, 4, 4, 5, 5];
        for (var field = 0; field < 8; field++)
        {
            foreach (var offset in new[] { -1, 0, 1 })
            {
                var bounds = new long?[8];
                bounds[field] = actual[field] + offset;
                var filter = ProvenanceFilter(bounds);
                var matches = field % 2 == 0 ? offset <= 0 : offset >= 0;
                filter.Matches(row).Should().Be(matches);
                var page = new SavedDraftPage(SessionListFilter.All, [row], null, 1, 0, false, MetadataFilter: filter);
                if (matches) { page.Validate(); }
                else { page.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>(); }
                if (offset == 0) { digests.Add(filter.Digest).Should().BeTrue(); }
            }
        }
        var exact = ProvenanceFilter([2, 2, 3, 3, 4, 4, 5, 5]);
        exact.Matches(row).Should().BeTrue();
        exact.Matches(row with { DraftRevision = 1 }).Should().BeFalse();
        exact.Matches(row with { Utf8Bytes = 3 }).Should().BeFalse();
        exact.Matches(row with { UnicodeScalars = 2 }).Should().BeFalse();
        exact.Matches(row with { Source = Guid.NewGuid() }).Should().BeFalse();
        exact.Matches(row with { Profile = new(Guid.NewGuid()) }).Should().BeFalse();
        var maximal = row with { Session = row.Session with { Generation = new(long.MaxValue) },
            Metadata = row.Metadata! with { Revision = new(long.MaxValue) },
            ControlRevision = long.MaxValue, AuthorityCeiling = long.MaxValue };
        ProvenanceFilter(Enumerable.Repeat<long?>(long.MaxValue, 8).ToArray()).Matches(maximal).Should().BeTrue();
        new SavedDraftMetadataFilter(null, null, null, minimumControlRevision: 0, maximumControlRevision: 0)
            .Matches(row with { ControlRevision = 0 }).Should().BeTrue();
    }

    [Fact]
    public void UnnamedRowsNeverMatchEitherNamedRevisionBoundButOtherRangesStillApply()
    {
        var row = ProvenanceRow() with { Metadata = null };
        SavedDraftMetadataFilter.Any.Matches(row).Should().BeTrue();
        ProvenanceFilter([2, 2, null, null, 4, 4, 5, 5]).Matches(row).Should().BeTrue();
        ProvenanceFilter([null, null, 1, null, null, null, null, null]).Matches(row).Should().BeFalse();
        ProvenanceFilter([null, null, null, long.MaxValue, null, null, null, null]).Matches(row).Should().BeFalse();
        ProvenanceFilter([null, null, 1, long.MaxValue, null, null, null, null]).Matches(row).Should().BeFalse();
    }

    [Fact]
    public void InvalidSummariesFailBeforeAnyPredicateCanExcludeThem()
    {
        var row = ProvenanceRow();
        foreach (var invalid in new[]
        {
            row with { Source = Guid.Empty }, row with { Profile = default },
            row with { Session = row.Session with { Generation = default } },
            row with { DraftRevision = 0 }, row with { Utf8Bytes = 0 }, row with { UnicodeScalars = 0 },
            row with { ControlRevision = -1 }, row with { AuthorityCeiling = 0 },
            row with { AuthorityDigest = new string('A', 64) },
            row with { Metadata = row.Metadata! with { SessionId = new(Guid.NewGuid()) } },
            row with { Metadata = row.Metadata! with { Revision = default } },
            row with { Metadata = row.Metadata! with { Name = null! } },
        })
        {
            var match = () => new SavedDraftMetadataFilter(99, null, null).Matches(invalid);
            match.Should().Throw<Exception>();
            var any = () => SavedDraftMetadataFilter.Any.Matches(invalid);
            any.Should().Throw<Exception>();
        }
    }
}
