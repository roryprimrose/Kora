using AwesomeAssertions;

using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Voice;

public sealed class ProvisionalTranscriptSnapshotTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData(" \t\r\n", true)]
    [InlineData("e\u0301\U0001f642", true)]
    [InlineData("high", false)]
    [InlineData("low", false)]
    [InlineData("split", false)]
    [InlineData("\0", false)]
    [InlineData("\uffff", false)]
    public void CompleteSnapshotsAreStrictUnicodeWithoutRepair(string text, bool valid)
    {
        text = text switch { "high" => "\ud800", "low" => "\udc00", "split" => "\ud800x", _ => text };
        new ProvisionalTranscriptSnapshot(7, 1, text).IsValid().Should().Be(valid);
    }

    [Theory]
    [InlineData(512, 'a', true)]
    [InlineData(513, 'a', false)]
    [InlineData(512, '\u00e9', true)]
    [InlineData(342, '\u0800', false)]
    [InlineData(341, '\u0800', true)]
    public void SnapshotsRejectExactCharacterAndUtf8Overflows(int count, char value, bool valid) =>
        new ProvisionalTranscriptSnapshot(7, 1, new(value, count)).IsValid().Should().Be(valid);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnorderedSnapshotsAreInvalid(long sequence) =>
        new ProvisionalTranscriptSnapshot(7, sequence, "text").IsValid().Should().BeFalse();
}
