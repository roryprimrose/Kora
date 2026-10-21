using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.Artifacts;

public sealed class OwnedDraftLineIndexTests
{
    [Theory]
    [InlineData("", new[] { 0 })]
    [InlineData("abc", new[] { 0 })]
    [InlineData("\r\n", new[] { 0, 2 })]
    [InlineData("\r", new[] { 0, 1 })]
    [InlineData("\n", new[] { 0, 1 })]
    [InlineData("a\r\nb\nc\rd\r\n", new[] { 0, 3, 5, 7, 10 })]
    [InlineData("\r\r\n\n", new[] { 0, 1, 3, 4 })]
    [InlineData("\u00e9\U0001f600\r\n\u2028\u2029\u0085", new[] { 0, 5 })]
    public void MapsExactLogicalLinesWithoutRewritingOrUnicodeSplitting(string source, int[] starts)
    {
        var index = new OwnedDraftLineIndex(source);
        index.Count.Should().Be(starts.Length);
        for (var line = 1; line <= starts.Length; line++)
        {
            index.TryResolve(line.ToString(CultureInfo.InvariantCulture), out var offset).Should().BeTrue();
            offset.Should().Be(starts[line - 1]);
        }
        index.TryResolve((starts.Length + 1).ToString(CultureInfo.InvariantCulture), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1\n")]
    [InlineData("1.0")]
    [InlineData("1x")]
    [InlineData("\u0661")]
    [InlineData("\uff11")]
    [InlineData("2147483647")]
    [InlineData("2147483648")]
    [InlineData("999999999999999999999999999999999999")]
    public void RejectsNoncanonicalAndOutOfBoundsInputsWithoutRepair(string input)
    {
        new OwnedDraftLineIndex("\n").TryResolve(input, out var offset).Should().BeFalse();
        offset.Should().Be(0);
    }

    [Fact]
    public void ExactCharacterAndLineBoundsAreNotTruncatedAndVeryLongRequestsCannotOverflow()
    {
        var source = new string('\n', ArtifactDefinition.MaximumContentLength);
        var index = new OwnedDraftLineIndex(source);
        index.Count.Should().Be(ArtifactDefinition.MaximumContentLength + 1);
        index.TryResolve(index.Count.ToString(CultureInfo.InvariantCulture), out var offset).Should().BeTrue();
        offset.Should().Be(source.Length);
        index.TryResolve("65538", out _).Should().BeFalse();
        index.TryResolve(new string('9', 100_000), out _).Should().BeFalse();
        index.TryResolve("1" + new string('0', 100_000), out _).Should().BeFalse();
        var tooLarge = () => new OwnedDraftLineIndex(source + "\n");
        tooLarge.Should().Throw<InvalidDataException>();
        var nullSource = () => new OwnedDraftLineIndex(null!);
        nullSource.Should().Throw<ArgumentNullException>();
        var nullRequest = () => index.TryResolve(null!, out _);
        nullRequest.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UnpairedSurrogateCodeUnitsRetainExactOffsets()
    {
        var index = new OwnedDraftLineIndex("\u00e9\U0001f600\r\n\ud800\n\udc00\r\u2028\u2029\u0085");
        index.Count.Should().Be(4);
        index.TryResolve("2", out var second).Should().BeTrue();
        second.Should().Be(5);
        index.TryResolve("3", out var third).Should().BeTrue();
        third.Should().Be(7);
        index.TryResolve("4", out var fourth).Should().BeTrue();
        fourth.Should().Be(9);
    }
}
