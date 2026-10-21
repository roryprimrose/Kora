using System.Text;

using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.Artifacts;

public sealed class SourceBufferSizeTests
{
    [Theory]
    [InlineData("", 0, 0, 0)]
    [InlineData("abc", 3, 3, 3)]
    [InlineData("\u00e9", 1, 2, 1)]
    [InlineData("\ud83d\ude00", 2, 4, 1)]
    [InlineData("e\u0301", 2, 3, 2)]
    [InlineData("\r\n\n\r", 4, 4, 4)]
    [InlineData("\ufeffa", 2, 4, 2)]
    [InlineData("\0", 1, 1, 1)]
    public void MeasuresExactBufferWithoutParsingOrNormalizing(string text, int units, int bytes, int scalars)
    {
        ArtifactFileDocument.MeasureSourceBuffer(text).Should().Be(new SourceBufferSize(units, bytes, scalars));
        ArtifactFileDocument.Encode(text, false).Should().HaveCount(bytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IllFormedUnicodeKeepsCodeUnitsButNeverInventsBytesOrScalars(int variant)
    {
        var text = variant switch
        {
            0 => new string('\ud800', 1),
            1 => new string('\udfff', 1),
            2 => "prefix" + '\ud800' + "suffix",
            _ => new string(['\udfff', '\ud800']),
        };
        ArtifactFileDocument.MeasureSourceBuffer(text).Should().Be(new SourceBufferSize(text.Length, null, null));
        ((Action)(() => ArtifactFileDocument.Encode(text, false))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void SourceByteCeilingIsNotACharacterOrScalarLimitAndOversizeStillMeasures(int delta)
    {
        var text = new string('\u00e9', ArtifactDefinition.MaximumContentLength / 2) + new string('a', Math.Max(delta, 0));
        if (delta < 0) { text = text[1..] + "a"; }
        var size = ArtifactFileDocument.MeasureSourceBuffer(text);
        size.Utf8Bytes.Should().Be(ArtifactDefinition.MaximumContentLength + delta);
        size.Utf16CodeUnits.Should().Be(text.Length);
        size.UnicodeScalars.Should().Be(text.Length);
        Encoding.UTF8.GetByteCount(text).Should().Be(size.Utf8Bytes);
    }

    [Fact]
    public void NullIsAProgrammingErrorNotAnEmptyBuffer()
    {
        ((Action)(() => ArtifactFileDocument.MeasureSourceBuffer(null!))).Should().Throw<ArgumentNullException>();
    }
}
