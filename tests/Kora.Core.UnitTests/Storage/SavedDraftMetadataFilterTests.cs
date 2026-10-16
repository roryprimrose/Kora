using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SavedDraftMetadataFilterTests
{
    private const string SourceId = "abcdef12-3456-7890-abcd-ef1234567890";
    private const string ProfileId = "fedcba98-7654-3210-fedc-ba9876543210";

    [Theory]
    [InlineData("", "")]
    [InlineData(SourceId, "")]
    [InlineData("", ProfileId)]
    [InlineData(SourceId, ProfileId)]
    public void OptionalGuidPredicatesRoundTripAndMatchExactSummaryOnly(string source, string profile)
    {
        var row = new SavedDraftSummary(Guid.Parse(SourceId), new(Guid.Parse(ProfileId)),
            new(new(Guid.NewGuid()), new(1), true), null, 2, 4, 1, 1, 1, new string('a', 64));
        row.Validate();
        var filter = SavedDraftMetadataFilter.Parse("2", "4", "4", "1", "1", source, profile);
        filter.Source.Should().Be(source.Length == 0 ? null : row.Source);
        filter.Profile.Should().Be(profile.Length == 0 ? null : row.Profile);
        SavedDraftMetadataFilter.IsValid("2", "4", "4", "1", "1", source, profile).Should().BeTrue();
        JsonSerializer.Deserialize<SavedDraftMetadataFilter>(JsonSerializer.Serialize(filter)).Should().Be(filter);
        filter.Matches(row).Should().BeTrue();
        filter.Matches(row with { Source = Guid.NewGuid() }).Should().Be(source.Length == 0);
        filter.Matches(row with { Profile = new(Guid.NewGuid()) }).Should().Be(profile.Length == 0);
        filter.Matches(row with { DraftRevision = 1 }).Should().BeFalse();
        filter.Matches(row with { Utf8Bytes = 3 }).Should().BeFalse();
        filter.Matches(row with { Utf8Bytes = 5 }).Should().BeFalse();
        filter.Matches(row with { UnicodeScalars = 2 }).Should().BeFalse();
        var page = new SavedDraftPage(SessionListFilter.All, [row], null, 1, 0, false, MetadataFilter: filter);
        page.Validate();
        if (source.Length != 0)
        {
            (page with { Records = [row with { Source = Guid.NewGuid() }] })
                .Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        }
        if (profile.Length != 0)
        {
            (page with { Records = [row with { Profile = new(Guid.NewGuid()) }] })
                .Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ABCDEF12-3456-7890-ABCD-EF1234567890")]
    [InlineData("{abcdef12-3456-7890-abcd-ef1234567890}")]
    [InlineData("(abcdef12-3456-7890-abcd-ef1234567890)")]
    [InlineData("abcdef1234567890abcdef1234567890")]
    [InlineData("abcdef12")]
    [InlineData("abcdef12-3456-7890-abcd-ef123456789")]
    [InlineData(" abcdef12-3456-7890-abcd-ef1234567890")]
    [InlineData("abcdef12-3456-7890-abcd-ef1234567890 ")]
    [InlineData("\tabcdef12-3456-7890-abcd-ef1234567890")]
    [InlineData("abcdef12-3456-7890-abcd-ef1234567890\n")]
    [InlineData("abcdef12-3456-7890-abcd-ef1234567890\0")]
    [InlineData(" ")]
    [InlineData("\u200b")]
    [InlineData("\uD800")]
    public void EveryMalformedNonemptyGuidRefusesBothFieldsWithoutRepair(string text)
    {
        SavedDraftMetadataFilter.IsValid("", "", "", source: text).Should().BeFalse();
        SavedDraftMetadataFilter.IsValid("", "", "", profile: text).Should().BeFalse();
        var parseSource = () => SavedDraftMetadataFilter.Parse("", "", "", source: text);
        var parseProfile = () => SavedDraftMetadataFilter.Parse("", "", "", profile: text);
        parseSource.Should().Throw<InvalidDataException>();
        parseProfile.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void TypedEmptyGuidPredicatesAndNullNativeFieldsFailClosed()
    {
        var source = () => new SavedDraftMetadataFilter(null, null, null, source: Guid.Empty);
        var profile = () => new SavedDraftMetadataFilter(null, null, null, profile: default(HostId<DeviceProfileIdentity>));
        source.Should().Throw<InvalidDataException>();
        profile.Should().Throw<InvalidDataException>();
        var parseSource = () => SavedDraftMetadataFilter.Parse("", "", "", source: null!);
        var parseProfile = () => SavedDraftMetadataFilter.Parse("", "", "", profile: null!);
        parseSource.Should().Throw<ArgumentNullException>();
        parseProfile.Should().Throw<ArgumentNullException>();
        SavedDraftMetadataFilter.Parse("", "", "", source: "", profile: "").Should().Be(SavedDraftMetadataFilter.Any);
    }

    [Fact]
    public void FilterDigestBindsEveryOptionalPredicateWithoutNativeText()
    {
        var filters = new SavedDraftMetadataFilter[]
        {
            SavedDraftMetadataFilter.Any, new(1, null, null), new(null, 0, null), new(null, null, 0),
            new(null, null, null, 0), new(null, null, null, null, 0),
            new(null, null, null, source: Guid.Parse(SourceId)),
            new(null, null, null, profile: new(Guid.Parse(ProfileId))),
            SavedDraftMetadataFilter.Parse("2", "4", "4", "1", "1", SourceId, ProfileId),
        };
        filters.Select(filter => filter.Digest).Should().OnlyHaveUniqueItems();
        filters.Should().OnlyContain(filter => filter.Digest.Length == 64
            && filter.Digest.All(char.IsAsciiHexDigitLower));
        SavedDraftMetadataFilter.Parse("0002", "04", "04", "01", "01", SourceId, ProfileId)
            .Digest.Should().Be(filters[^1].Digest, "existing numeric leading-zero semantics are unchanged");
    }

    [Theory]
    [InlineData("", "", "", null, null, null)]
    [InlineData("1", "", "", 1L, null, null)]
    [InlineData("9223372036854775807", "0", "16384", long.MaxValue, 0L, 16384L)]
    [InlineData("", "4", "", null, 4L, null)]
    [InlineData("", "", "4", null, null, 4L)]
    [InlineData("0001", "4", "4", 1L, 4L, 4L)]
    [InlineData("", "16384", "16384", null, 16384L, 16384L)]
    [InlineData("", "0", "0", null, 0L, 0L)]
    public void StrictOptionalFieldsRoundTripWithoutRepair(string revision, string minimum, string maximum,
        long? expectedRevision, long? expectedMinimum, long? expectedMaximum)
    {
        var filter = SavedDraftMetadataFilter.Parse(revision, minimum, maximum);
        filter.Should().Be(new SavedDraftMetadataFilter(expectedRevision, expectedMinimum, expectedMaximum));
        filter.DraftRevision.Should().Be(expectedRevision);
        filter.MinimumUtf8Bytes.Should().Be(expectedMinimum);
        filter.MaximumUtf8Bytes.Should().Be(expectedMaximum);
        SavedDraftMetadataFilter.IsValid(revision, minimum, maximum).Should().BeTrue();
        JsonSerializer.Deserialize<SavedDraftMetadataFilter>(JsonSerializer.Serialize(filter)).Should().Be(filter);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("1,000")]
    [InlineData("1_000")]
    [InlineData("1.0")]
    [InlineData("1e2")]
    [InlineData("0x1")]
    [InlineData("1\n")]
    [InlineData("1\0")]
    [InlineData("1\0\0")]
    [InlineData("\t1")]
    [InlineData("１")]
    [InlineData("١")]
    [InlineData("\uD800")]
    [InlineData("9223372036854775808")]
    public void EveryNonemptyInvalidIntegerRefusesAllFiveFields(string text)
    {
        SavedDraftMetadataFilter.IsValid(text, "", "").Should().BeFalse();
        SavedDraftMetadataFilter.IsValid("", text, "").Should().BeFalse();
        SavedDraftMetadataFilter.IsValid("", "", text).Should().BeFalse();
        SavedDraftMetadataFilter.IsValid("", "", "", text, "").Should().BeFalse();
        SavedDraftMetadataFilter.IsValid("", "", "", "", text).Should().BeFalse();
        var parse = () => SavedDraftMetadataFilter.Parse(text, "", "");
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("", "", null, null)]
    [InlineData("0", "", 0L, null)]
    [InlineData("", "0", null, 0L)]
    [InlineData("0", "0", 0L, 0L)]
    [InlineData("1", "", 1L, null)]
    [InlineData("", "1", null, 1L)]
    [InlineData("0001", "0001", 1L, 1L)]
    [InlineData("0", "4096", 0L, 4096L)]
    [InlineData("4096", "", 4096L, null)]
    [InlineData("", "4096", null, 4096L)]
    [InlineData("4096", "4096", 4096L, 4096L)]
    public void ScalarRangesRoundTripWithRevisionAndBytes(string minimum, string maximum,
        long? expectedMinimum, long? expectedMaximum)
    {
        var filter = SavedDraftMetadataFilter.Parse("2", "4", "16384", minimum, maximum);
        filter.Should().Be(new SavedDraftMetadataFilter(2, 4, 16384, expectedMinimum, expectedMaximum));
        filter.MinimumUnicodeScalars.Should().Be(expectedMinimum);
        filter.MaximumUnicodeScalars.Should().Be(expectedMaximum);
        SavedDraftMetadataFilter.IsValid("2", "4", "16384", minimum, maximum).Should().BeTrue();
        JsonSerializer.Deserialize<SavedDraftMetadataFilter>(JsonSerializer.Serialize(filter)).Should().Be(filter);
        ConversationText.MaximumUtf16Characters.Should().Be(4096);
    }

    [Theory]
    [InlineData("4097", "")]
    [InlineData("", "4097")]
    [InlineData("2", "1")]
    [InlineData("9223372036854775807", "")]
    [InlineData("", "9223372036854775807")]
    public void ScalarRangeNeverClampsOrRepairs(string minimum, string maximum)
    {
        SavedDraftMetadataFilter.IsValid("", "", "", minimum, maximum).Should().BeFalse();
        var parse = () => SavedDraftMetadataFilter.Parse("", "", "", minimum, maximum);
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(-1L, null)]
    [InlineData(4097L, null)]
    [InlineData(null, -1L)]
    [InlineData(null, 4097L)]
    [InlineData(2L, 1L)]
    public void TypedScalarBoundsRejectInvalidValues(long? minimum, long? maximum)
    {
        var create = () => new SavedDraftMetadataFilter(null, null, null, minimum, maximum);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(null, null, 1L, true)]
    [InlineData(0L, null, 1L, true)]
    [InlineData(null, 0L, 1L, false)]
    [InlineData(0L, 0L, 1L, false)]
    [InlineData(1L, null, 1L, true)]
    [InlineData(2L, null, 1L, false)]
    [InlineData(null, 1L, 1L, true)]
    [InlineData(null, 1L, 2L, false)]
    [InlineData(1L, 1L, 1L, true)]
    [InlineData(1L, 2L, 2L, true)]
    [InlineData(0L, 4096L, 4096L, true)]
    [InlineData(4096L, null, 4096L, true)]
    [InlineData(null, 4096L, 4096L, true)]
    [InlineData(4096L, 4096L, 4096L, true)]
    [InlineData(4096L, 4096L, 4095L, false)]
    public void ScalarMatchingUsesOnlyInclusiveValidatedSummary(long? minimum, long? maximum, long scalars, bool matches)
    {
        var row = new SavedDraftSummary(Guid.NewGuid(), new(Guid.NewGuid()),
            new(new(Guid.NewGuid()), new(1), true), null, 2, 16384, scalars, 1, 1, new string('a', 64));
        row.Validate();
        var filter = new SavedDraftMetadataFilter(2, 16384, 16384, minimum, maximum);
        filter.Matches(row).Should().Be(matches);
        filter.Matches(row with { DraftRevision = 1 }).Should().BeFalse();
        filter.Matches(row with { Utf8Bytes = 16383 }).Should().BeFalse();
        var page = new SavedDraftPage(SessionListFilter.All, [row], null, 1, 0, false, MetadataFilter: filter);
        if (matches) { page.Validate(); }
        else { page.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>(); }
    }

    [Theory]
    [InlineData("0", "", "")]
    [InlineData("", "16385", "")]
    [InlineData("", "", "16385")]
    [InlineData("", "5", "4")]
    public void OutOfRangeOrInvertedFieldsRefuseSearch(string revision, string minimum, string maximum) =>
        SavedDraftMetadataFilter.IsValid(revision, minimum, maximum).Should().BeFalse();

    [Theory]
    [InlineData(-1L, null, null)]
    [InlineData(0L, null, null)]
    [InlineData(null, -1L, null)]
    [InlineData(null, 16385L, null)]
    [InlineData(null, null, -1L)]
    [InlineData(null, null, 16385L)]
    [InlineData(null, 5L, 4L)]
    public void TypedContractRejectsInvalidBounds(long? revision, long? minimum, long? maximum)
    {
        var create = () => new SavedDraftMetadataFilter(revision, minimum, maximum);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(null, null, null, 1L, 4L, true)]
    [InlineData(1L, null, null, 1L, 4L, true)]
    [InlineData(2L, null, null, 1L, 4L, false)]
    [InlineData(null, 4L, null, 1L, 4L, true)]
    [InlineData(null, 5L, null, 1L, 4L, false)]
    [InlineData(null, null, 4L, 1L, 4L, true)]
    [InlineData(null, null, 3L, 1L, 4L, false)]
    [InlineData(1L, 4L, 4L, 1L, 4L, true)]
    [InlineData(null, 0L, 0L, 1L, 4L, false)]
    [InlineData(long.MaxValue, 16384L, 16384L, long.MaxValue, 16384L, true)]
    [InlineData(null, 0L, 16384L, 1L, 1L, true)]
    public void MatchingUsesOnlyInclusiveValidatedRevisionAndByteMetadata(long? revision, long? minimum,
        long? maximum, long rowRevision, long bytes, bool matches)
    {
        var row = new SavedDraftSummary(Guid.NewGuid(), new(Guid.NewGuid()),
            new(new(Guid.NewGuid()), new(1), true), null, rowRevision, bytes, 1, 1, 1, new string('a', 64));
        row.Validate();
        var filter = new SavedDraftMetadataFilter(revision, minimum, maximum);
        filter.Matches(row).Should().Be(matches);
        var page = new SavedDraftPage(SessionListFilter.All, [row], null, 1, 0, false, MetadataFilter: filter);
        if (matches) { page.Validate(); }
        else { page.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>(); }
    }

    [Fact]
    public void AnyIsEmptyAndNullIsNotAnEmptyNativeField()
    {
        SavedDraftMetadataFilter.Any.Should().Be(SavedDraftMetadataFilter.Parse("", "", ""));
        foreach (var fields in new (string, string, string)[] { (null!, "", ""), ("", null!, ""), ("", "", null!) })
        {
            var parse = () => SavedDraftMetadataFilter.Parse(fields.Item1, fields.Item2, fields.Item3);
            parse.Should().Throw<ArgumentNullException>();
        }
        foreach (var fields in new (string, string)[] { (null!, ""), ("", null!) })
        {
            var parse = () => SavedDraftMetadataFilter.Parse("", "", "", fields.Item1, fields.Item2);
            parse.Should().Throw<ArgumentNullException>();
        }
        ConversationText.MaximumUtf8Bytes.Should().Be(16384);
    }
}
