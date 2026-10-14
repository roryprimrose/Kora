using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SavedDraftMetadataFilterTests
{
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
    public void EveryNonemptyInvalidIntegerRefusesAllThreeFields(string text)
    {
        SavedDraftMetadataFilter.IsValid(text, "", "").Should().BeFalse();
        SavedDraftMetadataFilter.IsValid("", text, "").Should().BeFalse();
        SavedDraftMetadataFilter.IsValid("", "", text).Should().BeFalse();
        var parse = () => SavedDraftMetadataFilter.Parse(text, "", "");
        parse.Should().Throw<InvalidDataException>();
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
        ConversationText.MaximumUtf8Bytes.Should().Be(16384);
    }
}
