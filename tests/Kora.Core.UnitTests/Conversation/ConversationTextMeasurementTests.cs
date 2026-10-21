using AwesomeAssertions;

using Kora.Core.Conversation;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ConversationTextMeasurementTests
{
    [Theory]
    [InlineData("", 0, 0, 0, true)]
    [InlineData(" \t\r\n", 4, 4, 4, true)]
    [InlineData("e\u0301", 2, 3, 2, false)]
    [InlineData("\ud83d\ude42", 2, 4, 1, false)]
    [InlineData("\u0800", 1, 3, 1, false)]
    public void ExactCountsAndEmptySaveVersusSubmitSemantics(string body, int units, int bytes, int scalars, bool whiteSpace)
    {
        var result = ConversationText.Measure(body);
        result.Utf16CodeUnits.Should().Be(units);
        result.Utf8Bytes.Should().Be(bytes);
        result.UnicodeScalars.Should().Be(scalars);
        result.IsWhiteSpace.Should().Be(whiteSpace);
        result.IsWellFormed.Should().BeTrue();
        result.IsWithinLimits.Should().BeTrue();
        result.HasUnsupportedContent.Should().BeFalse();
        result.IsSaveShapeValid.Should().BeTrue();
        result.IsSubmissionShapeValid.Should().Be(!whiteSpace);
    }

    [Theory]
    [InlineData("a", 4096, 4096, 4096, true)]
    [InlineData("a", 4097, 4097, 4097, false)]
    [InlineData("\u0800", 4096, 12288, 4096, true)]
    [InlineData("\u0800", 4097, 12291, 4097, false)]
    [InlineData("\ud83d\ude42", 2048, 8192, 2048, true)]
    [InlineData("\ud83d\ude42", 2049, 8196, 2049, false)]
    [InlineData("a", 16384, 16384, 16384, false)]
    [InlineData("a", 16385, 16385, 16385, false)]
    public void IndependentExactBoundariesNeverTruncate(string unit, int repetitions, int bytes, int scalars, bool valid)
    {
        var body = string.Concat(Enumerable.Repeat(unit, repetitions));
        var result = ConversationText.Measure(body);
        result.Utf8Bytes.Should().Be(bytes);
        result.UnicodeScalars.Should().Be(scalars);
        result.Utf16CodeUnits.Should().Be(body.Length);
        result.IsWithinLimits.Should().Be(valid);
        result.IsSaveShapeValid.Should().Be(valid);
        result.IsSubmissionShapeValid.Should().Be(valid);
        if (valid) { ConversationText.Validate(body, submission: true); }
        else { body.Invoking(text => ConversationText.Validate(text)).Should().Throw<InvalidDataException>(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnpairedSurrogatesHaveNoReplacementCounts(bool high)
    {
        var body = "prefix" + (high ? "\ud800" : "\udc00") + "suffix";
        var result = ConversationText.Measure(body);
        result.Utf16CodeUnits.Should().Be(body.Length);
        result.Utf8Bytes.Should().BeNull();
        result.UnicodeScalars.Should().BeNull();
        result.IsWellFormed.Should().BeFalse();
        result.IsWithinLimits.Should().BeFalse();
        result.IsSaveShapeValid.Should().BeFalse();
        result.IsSubmissionShapeValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("\0")]
    [InlineData("\uffff")]
    public void UnsupportedContentIsCountedButNotShapeValid(string body)
    {
        var result = ConversationText.Measure(body);
        result.HasUnsupportedContent.Should().BeTrue();
        result.IsWithinLimits.Should().BeTrue();
        result.IsSaveShapeValid.Should().BeFalse();
        result.IsSubmissionShapeValid.Should().BeFalse();
    }

    [Fact]
    public void NullIsNotAnEmptyDraft()
    {
        var act = () => ConversationText.Measure(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
