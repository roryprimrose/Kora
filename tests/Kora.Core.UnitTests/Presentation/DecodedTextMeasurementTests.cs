using System.Text;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Core.UnitTests.Presentation;

public sealed class DecodedTextMeasurementTests
{
    [Theory]
    [InlineData("", 0, 0, 0)]
    [InlineData("ASCII", 5, 5, 5)]
    [InlineData("\u00e9\u65e5", 2, 2, 5)]
    [InlineData("\ud83d\ude42", 2, 1, 4)]
    [InlineData("e\u0301", 2, 2, 3)]
    [InlineData("\r\n\n\t", 4, 4, 4)]
    [InlineData("\ufeffa", 2, 2, 4)]
    public void MeasuresExactDecodedTextWithoutNormalizationOrLinePolicy(string text, int units, int scalars, int bytes)
    {
        var result = DecodedTextMeasurement.Measure(text);
        result.Utf16CodeUnits.Should().Be(units);
        result.UnicodeScalars.Should().Be(scalars);
        result.StrictUtf8Bytes.Should().Be(bytes);
    }

    [Theory]
    [InlineData(0xd800)]
    [InlineData(0xdc00)]
    public void InvalidUnicodeKeepsCodeUnitsButMakesScalarAndByteMeasurementsExplicitlyUnavailable(int invalid)
    {
        var result = DecodedTextMeasurement.Measure("a" + (char)invalid + "b");
        result.Utf16CodeUnits.Should().Be(3);
        result.UnicodeScalars.Should().BeNull();
        result.StrictUtf8Bytes.Should().BeNull();
        var missing = () => DecodedTextMeasurement.Measure(null!);
        missing.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MeasuresInclusiveAdmittedByteBoundaryWithoutInventingATextLimit(int offset)
    {
        var text = new string('a', LocalFilePolicy.MaximumBytes + offset);
        var file = File(Encoding.UTF8.GetBytes(text));
        var result = DecodedTextMeasurement.Measure(file.Text);
        result.Utf16CodeUnits.Should().Be(text.Length);
        result.UnicodeScalars.Should().Be(text.Length);
        result.StrictUtf8Bytes.Should().Be(text.Length);
        DecodedTextMeasurement.Measure(text + "a").StrictUtf8Bytes.Should().Be(text.Length + 1);
    }

    [Fact]
    public void AdmissionGuaranteesWellFormedTextAndOriginalBytesAreNotDecodedReencodingBytes()
    {
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("\ufeff\u00e9\ud83d\ude42")];
        var file = File(bytes);
        var result = DecodedTextMeasurement.Measure(file.Text);
        file.Text.Should().Be("\ufeff\u00e9\ud83d\ude42");
        result.UnicodeScalars.Should().Be(3);
        result.StrictUtf8Bytes.Should().Be(9);
        file.Review.Metadata.ByteLength.Should().Be(12);
        var invalidSurrogate = () => File([0xed, 0xa0, 0x80]);
        invalidSurrogate.Should().Throw<DecoderFallbackException>();
        // UTF-16 source files are not admitted by the existing strict UTF-8 contract.
        var utf16File = () => File([0xff, 0xfe, .. Encoding.Unicode.GetBytes("A")]);
        utf16File.Should().Throw<DecoderFallbackException>();
    }

    private static LocalFileRevision File(byte[] bytes) => new(new(Guid.NewGuid(), Guid.NewGuid(),
        HostRequest.Create(RequestOrigin.LocalUi), new(@"C:\Synthetic\text.txt", "synthetic", bytes.Length,
            DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
}
