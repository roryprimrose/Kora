using AwesomeAssertions;

using Kora.Core.Presentation.MarkdownStrikethrough;

namespace Kora.Core.UnitTests.Presentation;

public sealed class MarkdownStrikethroughTests
{
    [Fact]
    public void RangesUseExactUtf16OffsetsWithoutContentOrSourceMutation()
    {
        const string source = "😀 ~~e\u0301~~\r\n";
        var text = "😀 " + NativeMarkdownStrikethroughProfile.StartLabel + "e\u0301"
            + NativeMarkdownStrikethroughProfile.EndLabel;
        var span = new MarkdownStrikethroughSpan(3, 6,
            3 + NativeMarkdownStrikethroughProfile.StartLabel.Length, 2);
        source[span.SourceStart..span.SourceEnd].Should().Be("~~e\u0301~~");
        text[span.TextStart..span.TextEnd].Should().Be("e\u0301");
        NativeMarkdownStrikethroughProfile.Name.Should().Be("native-markdown-strikethrough-v4");
    }

    [Theory]
    [InlineData(-1, 4, 0, 1)]
    [InlineData(0, 3, 0, 1)]
    [InlineData(0, 4, -1, 1)]
    [InlineData(0, 4, 0, 0)]
    public void InvalidRangesAreExplicitlyRejected(int sourceStart, int sourceLength, int textStart, int textLength)
    {
        var create = () => new MarkdownStrikethroughSpan(sourceStart, sourceLength, textStart, textLength);
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(int.MaxValue, 4, 0, 1)]
    [InlineData(0, 4, int.MaxValue, 1)]
    public void OverflowCannotBecomeAValidRange(int sourceStart, int sourceLength, int textStart, int textLength)
    {
        var create = () => new MarkdownStrikethroughSpan(sourceStart, sourceLength, textStart, textLength);
        create.Should().Throw<OverflowException>();
    }
}
